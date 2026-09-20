namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// The read access the nested resolvers need. There are two implementations: one that batches through
/// GreenDonut DataLoaders and one that issues a query per parent, which is exactly what a naive
/// resolver does. The pair exists so the batching claim in ADR-002 can be measured with real query
/// counts (see <c>tests/Ledger.GraphQL.Tests/DataLoaderBatchingTests.cs</c>).
/// </summary>
public interface INestedReadGateway
{
    Task<AccountView?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntryView>> GetEntriesAsync(Guid transactionId, CancellationToken cancellationToken);
}

/// <summary>
/// Shared counterparty rule. A transaction can have more than two legs (the seeder produces 60/40
/// splits), and the schema exposes a single <c>counterpartyAccount</c>, so the dominant opposing leg —
/// the largest amount against the entry's direction — is treated as the counterparty. Documented in
/// the README as a deliberate simplification of a many-to-many relationship.
/// </summary>
internal static class CounterpartyResolver
{
    public static async Task<AccountView?> ResolveAsync(
        EntryView entry,
        INestedReadGateway gateway,
        CancellationToken cancellationToken)
    {
        var siblings = await gateway.GetEntriesAsync(entry.TransactionId, cancellationToken);

        var counterparty = siblings
            .Where(sibling => sibling.Id != entry.Id
                              && sibling.Direction != entry.Direction
                              && sibling.AccountId != entry.AccountId)
            .OrderByDescending(sibling => sibling.Amount)
            .FirstOrDefault();

        return counterparty is null
            ? null
            : await gateway.GetAccountAsync(counterparty.AccountId, cancellationToken);
    }
}