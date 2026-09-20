using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.GraphQL.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.GraphQL.Tests;

public class SmokeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Nested_statement_query_executes_end_to_end()
    {
        await using var factory = new ReadModelApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", factory.CreateToken(Ledger.GraphQL.Api.Security.AuthPolicies.Auditor));

        var discovery = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { accounts(first: 1) { edges { node { id name } } } }",
        });

        output.WriteLine("discovery status: " + discovery.StatusCode);
        output.WriteLine("discovery body: " + await discovery.Content.ReadAsStringAsync());

        var document = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        var accountId = document.RootElement
            .GetProperty("data").GetProperty("accounts").GetProperty("edges")[0]
            .GetProperty("node").GetProperty("id").GetString();

        Assert.False(string.IsNullOrWhiteSpace(accountId));

        var statement = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                query AccountStatement($id: ID!, $first: Int = 20) {
                  account(id: $id) {
                    name
                    balance { amount currency }
                    transactions(first: $first) {
                      edges { node { postedAt description entries { amount { amount currency } direction counterpartyAccount { name } } } }
                      pageInfo { hasNextPage }
                    }
                  }
                }
                """,
            variables = new { id = accountId, first = 20 },
        });

        output.WriteLine("statement status: " + statement.StatusCode);
        var payload = await statement.Content.ReadAsStringAsync();
        output.WriteLine("statement body: " + payload[..Math.Min(payload.Length, 1200)]);

        foreach (var record in factory.Logs.Records.TakeLast(25))
        {
            output.WriteLine(record);
        }

        Assert.Equal(HttpStatusCode.OK, statement.StatusCode);
    }
}