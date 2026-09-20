using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Ledger.Benchmark;

/// <summary>
/// The GraphQL-vs-REST comparison harness (plan s4/s5, architecture.md s4). It resolves the busiest
/// account in a seeded store, then fetches the same account statement through both surfaces:
/// <list type="bullet">
///   <item><b>GraphQL</b> - the persisted AccountStatement document, one round trip.</item>
///   <item><b>REST</b> - the read endpoints in RestReadEndpoints: account, transactions, then
///   entries per transaction, then counterparty accounts. The call sequence a REST client is forced
///   into because plain resources cannot embed a graph.</item>
/// </list>
/// For each leg it records round trips, payload bytes and p50/p95 latency, and - for GraphQL - the
/// server-side SQL statement count published as x-sql-queries. Results are written as a markdown
/// table suitable for committing to docs/benchmark-results.md.
/// <para>
/// Prerequisites: a running API with Observability:ExposeSqlQueryCount on (Development) and either
/// --token or the dev token endpoint available to mint one.
/// </para>
/// </summary>
internal static class Program
{
    private const string DiscoveryQuery = """
        query AccountDiscovery($first: Int = 6) {
          accounts(first: $first) {
            edges { node { id name } }
          }
        }
        """;

    private const string StatementQuery = """
        query AccountStatement($id: ID!, $first: Int = 20) {
          account(id: $id) {
            name
            balance { amount currency }
            transactions(first: $first) {
              edges {
                node {
                  postedAt
                  description
                  entries {
                    amount { amount currency }
                    direction
                    counterpartyAccount { name }
                  }
                }
              }
              pageInfo { hasNextPage endCursor }
            }
          }
        }
        """;

    public static async Task<int> Main(string[] args)
    {
        var options = BenchmarkOptions.Parse(args);

        using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await ResolveTokenAsync(http, options));

        await EnsureApiUpAsync(http);

        Console.WriteLine($"Base URL   : {options.BaseUrl}");
        Console.WriteLine($"Iterations : {options.Iterations} per leg");
        Console.WriteLine();

        // ---- pick a subject account worth measuring --------------------------------------------
        // Mirrors LedgerQueries.FindBusiestAccountAsync: the fixture spreads transactions unevenly, and
        // a statement that nests twenty transactions is far more convincing than one that nests zero.
        Console.WriteLine("Discovering the busiest account in the store...");
        var subject = await FindBusiestAccountAsync(http, options.First);
        Console.WriteLine($"  -> {subject.Id[..8]}... \"{subject.Name}\" - {subject.TransactionCount} transactions, {subject.EntryCount} entries");
        Console.WriteLine();

        // ---- GraphQL leg: one round trip, whatever the nesting ----------------------------------
        var graphql = await MeasureAsync(options.Iterations, async () =>
        {
            var response = await PostGraphQLAsync(http, StatementQuery, new { id = subject.Id, first = options.First });
            var body = await response.Content.ReadAsStringAsync();
            var sql = response.Headers.TryGetValues("x-sql-queries", out var values)
                      && int.TryParse(values.FirstOrDefault(), out var count)
                ? count
                : -1;
            return (body.Length, sql);
        });

        // ---- REST leg: the same statement as a walk of plain resources ---------------------------
        // Request sequence: 1 (account) + 1 (transactions) + T (entries per transaction) + M (counterparty
        // accounts). Every step needs the previous step's output, so none of them can be skipped or run
        // in parallel in a real client - that is the point of the comparison.
        var rest = await MeasureAsync(options.Iterations, async () =>
        {
            var bytes = 0;
            var requests = 0;

            async Task<JsonElement> GetAsync(string path)
            {
                requests++;
                var response = await http.GetAsync(path);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
                bytes += body.Length;
                return JsonSerializer.Deserialize<JsonElement>(body);
            }

            await GetAsync($"/rest/accounts/{subject.Id}");
            var transactions = await GetAsync($"/rest/accounts/{subject.Id}/transactions?limit={options.First}");
            var counterpartyIds = new HashSet<string>();

            foreach (var transaction in transactions.EnumerateArray())
            {
                var transactionId = transaction.GetProperty("id").GetString();
                var entries = await GetAsync($"/rest/transactions/{transactionId}/entries");

                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty("counterpartyAccountId", out var counterparty) &&
                        counterparty.ValueKind is JsonValueKind.String &&
                        counterparty.GetString() is { Length: > 0 } counterpartyId)
                    {
                        counterpartyIds.Add(counterpartyId);
                    }
                }
            }

            foreach (var counterpartyId in counterpartyIds)
            {
                await GetAsync($"/rest/accounts/{counterpartyId}");
            }

            return (bytes, requests);
        });

        // ---- report ------------------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine($"GraphQL : {graphql.RoundTrips} round trip,  payload {graphql.MedianBytes:N0} B,  p50 {graphql.P50:F1} ms,  p95 {graphql.P95:F1} ms,  SQL {graphql.SqlQueries} statement(s)");
        Console.WriteLine($"REST    : {rest.RoundTrips} round trips, payload {rest.MedianBytes:N0} B,  p50 {rest.P50:F1} ms,  p95 {rest.P95:F1} ms");

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            var report = RenderReport(options, subject, graphql, rest);
            var fullPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine();
            Console.WriteLine($"Report written to {fullPath}");
        }

        return 0;
    }

    private static async Task<string> ResolveTokenAsync(HttpClient http, BenchmarkOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            return options.Token;
        }

        // Dev-only fallback (spec s5/s10): mint a token via the dev STS endpoint so the benchmark can
        // run without the reviewer pasting a token in. Requires the Development environment.
        var response = await http.GetAsync($"/dev/token/{options.Role}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("Dev token endpoint returned no accessToken.");
    }

    private static async Task EnsureApiUpAsync(HttpClient http)
    {
        try
        {
            using var response = await http.GetAsync("/health");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The API is not reachable at {http.BaseAddress}. Start it first, e.g. " +
                "\"dotnet run --project src/Ledger.GraphQL.Api --launch-profile 'Ledger.Api (sqlite)'\".", ex);
        }
    }

    private static async Task<Subject> FindBusiestAccountAsync(HttpClient http, int first)
    {
        var discovery = await PostGraphQLAsync(http, DiscoveryQuery, new { first });
        var body = await discovery.Content.ReadFromJsonAsync<JsonElement>();

        var candidates = body.GetProperty("data")
            .GetProperty("accounts").GetProperty("edges")
            .EnumerateArray()
            .Select(edge => edge.GetProperty("node"))
            .Select(node => (Id: node.GetProperty("id").GetString()!, Name: node.GetProperty("name").GetString()!))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No accounts found in the store. Start the API so it seeds itself, or run \"dotnet run --seed\".");
        }

        var best = new Subject(candidates[0].Id, candidates[0].Name, 0, 0);

        foreach (var (id, name) in candidates)
        {
            var response = await PostGraphQLAsync(http, StatementQuery, new { id, first });
            var statement = (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("data").GetProperty("account");

            var edges = statement.GetProperty("transactions").GetProperty("edges").EnumerateArray().ToList();
            var entries = edges.Sum(edge => edge.GetProperty("node").GetProperty("entries").GetArrayLength());

            if (edges.Count > best.TransactionCount)
            {
                best = new Subject(id, name, edges.Count, entries);
            }
        }

        return best;
    }

    private static async Task<HttpResponseMessage> PostGraphQLAsync(HttpClient http, string query, object variables)
    {
        var response = await http.PostAsJsonAsync("/graphql", (object)new Dictionary<string, object?>
        {
            ["query"] = query,
            ["variables"] = variables,
        });
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<Measurement> MeasureAsync(int iterations, Func<Task<(int Bytes, int Extra)>> request)
    {
        var bytes = new List<int>(iterations);
        var latency = new List<double>(iterations);
        var extras = new List<int>(iterations);

        // One untimed warm-up so connection pooling, JIT and (for GraphQL) persisted-operation
        // registration don't skew the first sample.
        await request();

        for (var i = 0; i < iterations; i++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var (byteCount, extra) = await request();
            stopwatch.Stop();

            bytes.Add(byteCount);
            latency.Add(stopwatch.Elapsed.TotalMilliseconds);
            extras.Add(extra);
        }

        bytes.Sort();
        latency.Sort();

        return new Measurement(
            Bytes: bytes[(iterations - 1) / 2],
            P50: Percentile(latency, 50),
            P95: Percentile(latency, 95),
            ExtraMax: extras.Max(),
            LastExtra: extras[^1]);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var index = (int)Math.Ceiling(p / 100 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static string RenderReport(
        BenchmarkOptions options,
        Subject subject,
        Measurement graphql,
        Measurement rest)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Benchmark results - GraphQL vs REST on the same statement");
        builder.AppendLine();
        builder.AppendLine($"> Measured {options.Iterations} executions per leg against `{options.BaseUrl}` " +
            $"(seed scale: {options.SeedNote}). Subject: account {subject.Id[..8]}... ({subject.Name}) - " +
            $"{subject.TransactionCount} transactions, {subject.EntryCount} entries nested in the statement. " +
            "All legs fetched the same data: account, its 20 most recent transactions, every entry, and every counterparty account.");
        builder.AppendLine();

        builder.AppendLine("| Surface | HTTP round trips | Payload (median) | p50 | p95 | SQL statements |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        builder.AppendLine($"| GraphQL (1 AccountStatement query) | 1 | {graphql.Bytes:N0} B | {graphql.P50:F1} ms | {graphql.P95:F1} ms | {graphql.SqlQueries} |");
        builder.AppendLine($"| REST (account + transactions + entries + counterparties) | {rest.RoundTrips} | {rest.Bytes:N0} B | {rest.P50:F1} ms | {rest.P95:F1} ms | n/a |");
        builder.AppendLine();

        builder.AppendLine("The REST leg is not a strawman: each of its requests is the *minimum* a client can " +
            "make against plain resources, because the output of each step is the input of the next. " +
            "GraphQL collapses all of it into one round trip and one nested payload.");
        builder.AppendLine();

        builder.AppendLine("Reproduce with:");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine("# 1. start the API (seeds itself on first run)");
        builder.AppendLine("dotnet run --project src/Ledger.GraphQL.Api --launch-profile \"Ledger.Api (sqlite)\"");
        builder.AppendLine();
        builder.AppendLine("# 2. run the harness (Development exposes the dev token endpoint and x-sql-queries)");
        builder.AppendLine("dotnet run --project tools/Ledger.Benchmark -- --base-url http://localhost:5080 --iterations 30 --output docs/benchmark-results.md");
        builder.AppendLine("```");

        return builder.ToString();
    }

    private sealed record Subject(string Id, string Name, int TransactionCount, int EntryCount);

    /// <summary>
    /// One measured leg. Extra is leg-specific: for GraphQL it is the SQL statement count published
    /// as x-sql-queries; for REST it is the number of HTTP requests the leg made.
    /// </summary>
    private sealed record Measurement(
        int Bytes,
        double P50,
        double P95,
        int ExtraMax,
        int LastExtra)
    {
        public string SqlQueries => ExtraMax < 0 ? "n/a" : ExtraMax.ToString();
        public int MedianBytes => Bytes;
        public int RoundTrips => LastExtra;
    }

    private sealed class BenchmarkOptions
    {
        public string BaseUrl { get; private set; } = "http://localhost:5080";
        public string Token { get; private set; } = string.Empty;
        public string Role { get; private set; } = "auditor";
        public int Iterations { get; private set; } = 30;
        public int First { get; private set; } = 20;
        public string OutputPath { get; private set; } = string.Empty;
        public string SeedNote { get; internal set; } = "appsettings defaults";

        public static BenchmarkOptions Parse(string[] args)
        {
            var options = new BenchmarkOptions();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--base-url":
                        options.BaseUrl = RequireValue(args, ++i, "--base-url").TrimEnd('/');
                        break;
                    case "--token":
                        options.Token = RequireValue(args, ++i, "--token");
                        break;
                    case "--role":
                        options.Role = RequireValue(args, ++i, "--role").ToLowerInvariant();
                        break;
                    case "--iterations":
                        options.Iterations = int.Parse(RequireValue(args, ++i, "--iterations"));
                        break;
                    case "--first":
                        options.First = int.Parse(RequireValue(args, ++i, "--first"));
                        break;
                    case "--output":
                        options.OutputPath = RequireValue(args, ++i, "--output");
                        break;
                    case "--seed-note":
                        options.SeedNote = RequireValue(args, ++i, "--seed-note");
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'. Known: --base-url, --token, --role, --iterations, --first, --output, --seed-note.");
                }
            }

            return options;
        }

        private static string RequireValue(string[] args, int index, string name) =>
            index < args.Length
                ? args[index]
                : throw new ArgumentException($"Missing value for {name}.");
    }
}
