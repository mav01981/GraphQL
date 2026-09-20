using System.Net;
using System.Text.Json;
using Ledger.GraphQL.Api.Security;
using Ledger.GraphQL.Tests.Support;
using Xunit.Abstractions;

namespace Ledger.GraphQL.Tests;

/// <summary>
/// The field-level authorization demo (spec §5), asserted role by role: the protected fields null out
/// with <c>AUTH_NOT_AUTHORIZED</c> while the rest of the statement still resolves and the request
/// still answers <c>200</c>. That partial result is the behaviour a REST resource cannot express —
/// it has to answer 403 for the whole document instead.
/// </summary>
public class FieldAuthorizationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(AuthPolicies.Viewer, false, false, false)]
    [InlineData(AuthPolicies.Accountant, true, true, false)]
    [InlineData(AuthPolicies.Auditor, true, true, true)]
    public async Task Protected_fields_are_gated_per_role(string role, bool seeBalance, bool seeAmount, bool seeCounterparty)
    {
        await using var factory = new ReadModelApiFactory();
        var auditorClient = factory.CreateAuthenticatedClient();
        var account = await LedgerQueries.FindBusiestAccountAsync(auditorClient);

        var client = factory.CreateAuthenticatedClient(role);
        var response = await client.PostGraphQLAsync(LedgerQueries.AccountStatement, new { id = account.Id, first = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var statement = response.Data.GetProperty("account");
        var entries = statement.GetProperty("transactions").GetProperty("edges")
            .EnumerateArray()
            .SelectMany(edge => edge.GetProperty("node").GetProperty("entries").EnumerateArray())
            .ToList();

        Assert.NotEmpty(entries);

        // Unprotected data resolves for every role: authorization degrades per field, not per request.
        Assert.Equal(JsonValueKind.String, statement.GetProperty("name").ValueKind);
        Assert.All(entries, entry => Assert.Equal(JsonValueKind.String, entry.GetProperty("direction").ValueKind));

        AssertFieldAccess(statement.GetProperty("balance"), seeBalance, "Account.balance");
        Assert.All(entries, entry => AssertFieldAccess(entry.GetProperty("amount"), seeAmount, "Entry.amount"));
        Assert.All(entries, entry => AssertFieldAccess(entry.GetProperty("counterpartyAccount"), seeCounterparty, "Entry.counterpartyAccount"));

        var deniedFieldSelections = ((seeAmount ? 0 : 1) + (seeCounterparty ? 0 : 1)) * entries.Count
            + (seeBalance ? 0 : 1);

        output.WriteLine($"{role}: {response.ByteCount} bytes, {response.SqlStatements} SQL statement(s), " +
            $"{response.ErrorCodes.Count} error(s) [{(response.ErrorCodes.Count == 0 ? "none" : string.Join(",", response.ErrorCodes.Distinct()))}]");

        if (deniedFieldSelections == 0)
        {
            Assert.False(response.HasErrors);
        }
        else
        {
            // At least one denial per protected field that was actually selected; the code is what a
            // client switches on to render "you may not see this" rather than an empty cell.
            Assert.NotEmpty(response.ErrorCodes);
            Assert.All(response.ErrorCodes, code => Assert.Equal("AUTH_NOT_AUTHORIZED", code));
        }
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_at_the_endpoint()
    {
        await using var factory = new ReadModelApiFactory();

        var response = await factory.CreateClient().PostGraphQLAsync("query { accounts(first: 1) { edges { node { id } } } }");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void AssertFieldAccess(JsonElement field, bool allowed, string fieldName)
    {
        if (allowed)
        {
            Assert.Equal(JsonValueKind.Object, field.ValueKind);
        }
        else
        {
            Assert.True(
                field.ValueKind == JsonValueKind.Null,
                $"{fieldName} should have been withheld, but the response contained {field.GetRawText()}");
        }
    }
}
