using Ledger.ReadModel.Accounts;
using Ledger.ReadModel.Transactions;

namespace Ledger.ReadModel.Seeding;

/// <summary>A complete, internally consistent set of projections.</summary>
public sealed record ProjectionDataSet(
    IReadOnlyList<AccountRecord> Accounts,
    IReadOnlyList<TransactionRecord> Transactions,
    IReadOnlyList<EntryRecord> Entries);

/// <summary>
/// Extension point from spec §2: the projection store can be populated from synthetic data today and
/// from the ledger demo's event store in a future iteration without the GraphQL layer noticing.
/// </summary>
public interface IProjectionSource
{
    Task<ProjectionDataSet> GenerateAsync(SeedOptions options, CancellationToken cancellationToken);
}