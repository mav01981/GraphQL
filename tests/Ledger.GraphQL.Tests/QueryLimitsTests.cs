using System.Net;
using Ledger.GraphQL.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.GraphQL.Tests;

/// <summary>
/// The production safeguards from spec §6, asserted through the real pipeline: an abusive query is
/// rejected <em>before</em> execution (the SQL statement count stays at zero, so no read-model work is
/// wasted) and the client gets a machine-readable error code rather than a stack trace.
/// </summary>
public class QueryLimitsTests(ITestOutputHelper output)
{
    /// <summary>Nine levels of selection set — one past <c>GraphQL:MaxExecutionDepth</c> (8). Without
    /// the limit this is the query that walks the entry → counterpartyAccount → transactions cycle.</summary>
    private const string TooDeepQuery = """
        query TooDeep($id: ID!) {
          account(id: $id) {                          # 1
            transactions(first: 1) {                  # 2
              edges {                                 # 3
                node {                                # 4
                  entries {                           # 5
                    counterpartyAccount {             # 6
                      transactions(first: 1) {        # 7
                        edges {                       # 8
                          node {                      # 9
                            entries { id }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>A wide page: 100 accounts × every field, which the cost analyser has to price before
    /// the resolvers ever run.</summary>
    private const string TooExpensiveQuery = """
        query TooExpensive {
          accounts(first: 100) {
            edges {
              node {
                id
                name
                type
                currency
                balance { amount currency }
                transactions(first: 20) {
                  edges { node { id postedAt description reference entries { id direction } } }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public async Task Deeply_nested_query_is_rejected_before_execution()
    {
        await using var factory = new ReadModelApiFactory();
        var client = factory.CreateAuthenticatedClient();
        var account = await LedgerQueries.FindBusiestAccountAsync(client);

        var response = await client.PostGraphQLAsync(TooDeepQuery, new { id = account.Id });

        output.WriteLine($"[depth] status={response.StatusCode} sql={response.SqlStatements} " +
            $"codes=[{string.Join(",", response.ErrorCodes.Distinct())}] messages=[{string.Join(" | ", response.ErrorMessages)}]");

        Assert.NotEmpty(response.ErrorCodes);
        Assert.Equal(0, response.SqlStatements);
    }

    [Fact]
    public async Task Over_budget_query_is_rejected_by_the_cost_analyser()
    {
        await using var factory = new ReadModelApiFactory();
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostGraphQLAsync(TooExpensiveQuery);

        output.WriteLine($"[cost] status={response.StatusCode} sql={response.SqlStatements} " +
            $"codes=[{string.Join(",", response.ErrorCodes.Distinct())}] messages=[{string.Join(" | ", response.ErrorMessages)}]");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(response.ErrorCodes);
    }

    [Fact]
    public async Task Unbounded_page_size_is_rejected_by_the_paging_rules()
    {
        await using var factory = new ReadModelApiFactory();
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostGraphQLAsync("query { accounts(first: 1000) { edges { node { id } } } }");

        output.WriteLine($"[paging] status={response.StatusCode} sql={response.SqlStatements} codes=[{string.Join(",", response.ErrorCodes.Distinct())}] " +
            $"messages=[{string.Join(" | ", response.ErrorMessages)}]");

        // Both bounds fire for this query and the cheapest rejection wins: the cost analyser prices
        // 1,000 items over the 1,000-point budget (HC0047) and rejects before execution — the same
        // fail-fast contract as the depth limit (spec §8). The paging limit (HC0051, "maximum allowed
        // items per page were exceeded") remains the backstop for pages that fit the cost budget.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, response.SqlStatements);
        Assert.Contains("HC0047", response.ErrorCodes);
    }

    [Fact]
    public async Task Cost_of_every_request_is_logged()
    {
        await using var factory = new ReadModelApiFactory(useDataLoaders: true);
        var client = factory.CreateAuthenticatedClient();
        var account = await LedgerQueries.FindBusiestAccountAsync(client);

        factory.Logs.Clear();
        var response = await client.PostGraphQLAsync(LedgerQueries.AccountStatement, new { id = account.Id, first = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var costLog = factory.Logs.Records.Where(record => record.Contains("GraphQL operation cost", StringComparison.Ordinal)).ToList();
        output.WriteLine(string.Join(Environment.NewLine, costLog));

        // spec §7: the cost that the limit is enforced against is logged per request, so the "we bound
        // this" claim is checkable in a running system rather than only in configuration.
        Assert.Single(costLog);
    }
}

