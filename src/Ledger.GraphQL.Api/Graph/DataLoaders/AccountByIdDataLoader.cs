using GreenDonut;
using Ledger.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Ledger.GraphQL.Api.Graph.DataLoaders;

/// <summary>
/// Batches account lookups: the <c>counterpartyAccount</c> field, the <c>account</c> root field and
/// the currency lookup behind <c>Entry.amount</c> all funnel through here, so any number of sibling
/// requests in the same execution tick collapse into a single <c>WHERE id IN (...)</c> query.
/// </summary>
public sealed class AccountByIdDataLoader : BatchDataLoader<Guid, AccountView>
{
    private readonly IDbContextFactory<ReadDbContext> _dbFactory;

    public AccountByIdDataLoader(
        IDbContextFactory<ReadDbContext> dbFactory,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options ?? new DataLoaderOptions())
    {
        _dbFactory = dbFactory;
    }

    protected override async Task<IReadOnlyDictionary<Guid, AccountView>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(account => keys.Contains(account.Id))
            .ToListAsync(cancellationToken);

        return accounts.ToDictionary(
            account => account.Id,
            account => new AccountView(account.Id, account.Name, account.Type, account.Currency, account.Balance));
    }
}