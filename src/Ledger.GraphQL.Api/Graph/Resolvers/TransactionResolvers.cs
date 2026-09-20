using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// Resolvers added to the <c>Transaction</c> schema type. <c>entries</c> goes through the nested read
/// gateway, which means the batched (DataLoader) or naive implementation is chosen by configuration.
/// </summary>
[ExtendObjectType(typeof(TransactionView))]
public sealed class TransactionResolvers
{
    // Cost model (spec §7 / ADR-003): entries has no slicing argument (double-entry bounds each
    // transaction to a handful of entries), so it carries a flat weight instead of a list multiplier.
    [Cost(CostWeights.Entry)]
    public Task<IReadOnlyList<EntryView>> GetEntriesAsync(
        [Parent] TransactionView transaction,
        INestedReadGateway gateway,
        CancellationToken cancellationToken) =>
        gateway.GetEntriesAsync(transaction.Id, cancellationToken);
}