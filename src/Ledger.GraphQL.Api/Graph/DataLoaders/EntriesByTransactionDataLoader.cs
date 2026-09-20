using GreenDonut;
using Ledger.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Ledger.GraphQL.Api.Graph.DataLoaders;

/// <summary>
/// Batches entries per transaction. This is the loader that removes the server-side N+1 for
/// <c>Transaction.entries</c> (20 transactions → 1 query instead of 20), and it doubles as the
/// sibling lookup the counterparty resolver needs — GreenDonut caches the batch result for the
/// lifetime of the request, so the second read of the same transaction costs nothing.
/// </summary>
public sealed class EntriesByTransactionDataLoader : GroupedDataLoader<Guid, EntryView>
{
    private readonly IDbContextFactory<ReadDbContext> _dbFactory;

    public EntriesByTransactionDataLoader(
        IDbContextFactory<ReadDbContext> dbFactory,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options ?? new DataLoaderOptions())
    {
        _dbFactory = dbFactory;
    }

    protected override async Task<ILookup<Guid, EntryView>> LoadGroupedBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entries = await db.Entries
            .AsNoTracking()
            .Where(entry => keys.Contains(entry.TransactionId))
            .ToListAsync(cancellationToken);

        return entries.ToLookup(
            entry => entry.TransactionId,
            entry => new EntryView(entry.Id, entry.TransactionId, entry.AccountId, entry.Amount, entry.Direction));
    }
}