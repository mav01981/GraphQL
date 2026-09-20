using System.Net;
using Ledger.GraphQL.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.GraphQL.Tests;

/// <summary>
/// The before/after measurement behind ADR-002: the same nested statement query, the same schema,
/// executed against the DataLoader-backed nested read gateway and against the naive one. Only the
/// number of SQL statements changes — which is the entire argument for batching inside the resolver
/// layer instead of pushing the stitching onto the client.
/// </summary>
public class DataLoaderBatchingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Batched_gateway_issues_far_fewer_statements_than_the_naive_one()
    {
        await using var batchedFactory = new ReadModelApiFactory(useDataLoaders: true);
        await using var naiveFactory = new ReadModelApiFactory(useDataLoaders: false);

        var batchedClient = batchedFactory.CreateAuthenticatedClient();
        var naiveClient = naiveFactory.CreateAuthenticatedClient();

        // Both factories seed the same deterministic fixture, so the same account id exists in both.
        var account = await LedgerQueries.FindBusiestAccountAsync(batchedClient, output.WriteLine);

        var batched = await MeasureAsync(batchedClient, account.Id);
        var naive = await MeasureAsync(naiveClient, account.Id);

        output.WriteLine($"account {account.Id}: {account.Transactions} transactions, {account.Entries} entries");
        output.WriteLine($"batched: {batched.SqlStatements} SQL statement(s), {batched.ByteCount} bytes, {batched.ElapsedMs:0.0} ms");
        output.WriteLine($"naive:   {naive.SqlStatements} SQL statement(s), {naive.ByteCount} bytes, {naive.ElapsedMs:0.0} ms");

        Assert.Equal(HttpStatusCode.OK, batched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, naive.StatusCode);
        Assert.False(batched.HasErrors);

        // Same answer either way: the switch changes the query plan, not the data.
        Assert.Equal(account.Transactions, batched.Transactions);
        Assert.Equal(account.Transactions, naive.Transactions);

        // The naive gateway runs a query per parent — and again per counterparty and per currency
        // lookup — so its statement count grows with the page size; the batched one stays flat.
        Assert.True(account.Transactions >= 5, "the fixture needs a few transactions per account for this to be meaningful");
        Assert.True(
            batched.SqlStatements * 2 <= naive.SqlStatements,
            $"expected batching to at least halve the statement count (batched={batched.SqlStatements}, naive={naive.SqlStatements})");

        // Fixed upper bound on the batched path: the account, the account's (paged) transactions, one
        // batched entries query and one batched account query for currencies and counterparties.
        Assert.InRange(batched.SqlStatements, 1, 6);
    }

    [Fact]
    public async Task Batched_statement_query_returns_the_full_nested_payload_in_one_round_trip()
    {
        await using var factory = new ReadModelApiFactory(useDataLoaders: true);
        var client = factory.CreateAuthenticatedClient();

        var account = await LedgerQueries.FindBusiestAccountAsync(client, output.WriteLine);
        var response = await MeasureAsync(client, account.Id);

        // One request, whatever the nesting depth — the point of the whole demo. The assertion on the
        // payload is there to prove the single response is complete, not a stub.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.HasErrors);
        Assert.True(response.ByteCount > 1_000, $"expected a nested payload, got {response.ByteCount} bytes");
        Assert.Equal(account.Entries, response.Entries);
    }

    private static async Task<StatementMeasurement> MeasureAsync(HttpClient client, string accountId)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.PostGraphQLAsync(LedgerQueries.AccountStatement, new { id = accountId, first = 20 });
        stopwatch.Stop();

        var edges = response.Data
            .GetProperty("account").GetProperty("transactions").GetProperty("edges")
            .EnumerateArray()
            .ToList();

        return new StatementMeasurement(
            response,
            stopwatch.Elapsed.TotalMilliseconds,
            edges.Count,
            edges.Sum(edge => edge.GetProperty("node").GetProperty("entries").GetArrayLength()));
    }

    /// <summary>A statement response plus what it took to produce it, so one run yields both the
    /// query-count evidence and a latency figure.</summary>
    private sealed record StatementMeasurement(GraphQLResponse Response, double ElapsedMs, int Transactions, int Entries)
    {
        public int SqlStatements => Response.SqlStatements;

        public int ByteCount => Response.ByteCount;

        public HttpStatusCode StatusCode => Response.StatusCode;

        public bool HasErrors => Response.HasErrors;
    }
}
