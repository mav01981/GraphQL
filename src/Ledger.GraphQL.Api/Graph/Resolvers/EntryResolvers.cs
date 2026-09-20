using HotChocolate.Authorization;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using Ledger.GraphQL.Api.Security;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// Resolvers added to the <c>Entry</c> schema type. Both fields are the field-level authorization
/// demo: a viewer token still gets a 200 with the rest of the statement, just without these two
/// fields, while a REST resource would have to answer 403 for the whole document.
/// </summary>
[ExtendObjectType(typeof(EntryView))]
public sealed class EntryResolvers
{
    [Authorize(AuthPolicies.ViewAmount, ApplyPolicy.BeforeResolver)]
    public async Task<MoneyView?> GetAmountAsync(
        [Parent] EntryView entry,
        INestedReadGateway gateway,
        CancellationToken cancellationToken)
    {
        // The currency lives on the account, so this second read is served by the same DataLoader
        // batch (one extra query for the whole response, not one per entry).
        var account = await gateway.GetAccountAsync(entry.AccountId, cancellationToken);

        return MoneyView.From(entry.Amount, account?.Currency);
    }

    [Authorize(AuthPolicies.ViewCounterparty, ApplyPolicy.BeforeResolver)]
    // Cross-account visibility is the most sensitive field in the schema (spec §5) and also the
    // expensive one — it fans out to another account node — so it carries a deliberate weight.
    [Cost(CostWeights.Counterparty)]
    public Task<AccountView?> GetCounterpartyAccountAsync(
        [Parent] EntryView entry,
        INestedReadGateway gateway,
        CancellationToken cancellationToken) =>
        CounterpartyResolver.ResolveAsync(entry, gateway, cancellationToken);
}