namespace Ledger.GraphQL.Tests.Support;

/// <summary>An account picked from the deterministic fixture, with the nesting it actually exercises.</summary>
internal sealed record DiscoveredAccount(string Id, int Transactions, int Entries);

/// <summary>
/// The client-side documents the demo is built around. They live in one place so the schema shape the
/// tests depend on stays visible, and so the tests exercise the same query a client would send rather
/// than a test-only simplification.
/// </summary>
internal static class LedgerQueries
{
    public const string AccountDiscovery = """
        query AccountDiscovery($first: Int = 6) {
          accounts(first: $first) {
            edges { node { id name } }
          }
        }
        """;

    public const string AccountStatement = """
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
                    id
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

    /// <summary>
    /// Picks the busiest of the first few accounts: the fixture spreads transactions unevenly, and a
    /// statement that nests ten transactions is a far more convincing measurement than one that nests
    /// zero.
    /// </summary>
    public static async Task<DiscoveredAccount> FindBusiestAccountAsync(HttpClient client, Action<string>? log = null)
    {
        var discovery = await client.PostGraphQLAsync(AccountDiscovery);
        Assert.False(discovery.HasErrors, string.Join("; ", discovery.ErrorMessages));

        var ids = discovery.Data
            .GetProperty("accounts").GetProperty("edges")
            .EnumerateArray()
            .Select(edge => edge.GetProperty("node").GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        Assert.NotEmpty(ids);

        var best = new DiscoveredAccount(ids[0], 0, 0);

        foreach (var id in ids)
        {
            var response = await client.PostGraphQLAsync(AccountStatement, new { id, first = 20 });
            if (response.HasErrors)
            {
                log?.Invoke($"probe {id}: {response.StatusCode} {string.Join("; ", response.ErrorMessages)}");
                continue;
            }

            var edges = response.Data
                .GetProperty("account").GetProperty("transactions").GetProperty("edges")
                .EnumerateArray()
                .ToList();

            var entries = edges.Sum(edge => edge.GetProperty("node").GetProperty("entries").GetArrayLength());
            log?.Invoke($"probe {id} ({response.Data.GetProperty("account").GetProperty("name").GetString()}): " +
                $"{edges.Count} transactions, {entries} entries, {response.SqlStatements} SQL statement(s)");

            if (edges.Count > best.Transactions)
            {
                best = new DiscoveredAccount(id, edges.Count, entries);
            }
        }

        return best;
    }
}
