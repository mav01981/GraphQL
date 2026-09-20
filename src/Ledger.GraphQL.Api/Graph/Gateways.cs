using Ledger.GraphQL.Api.Graph.DataLoaders;
using Ledger.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>DataLoader-backed read path: batched within the execution tick (the shipping default).</summary>
internal sealed class DataLoaderNestedReadGateway(
    AccountByIdDataLoader accounts,
    EntriesByTransactionDataLoader entries) : INestedReadGateway
{
    public Task<AccountView?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        accounts.LoadAsync(accountId, cancellationToken);

    public async Task<IReadOnlyList<EntryView>> GetEntriesAsync(Guid transactionId, CancellationToken cancellationToken) =>
        await entries.LoadAsync(transactionId, cancellationToken) ?? [];
}

/// <summary>
/// Naive read path: one query per parent, i.e. the server-side N+1 this demo argues against. Kept in
/// the codebase on purpose — it is the "before" side of the batching measurement.
/// </summary>
internal sealed class NaiveNestedReadGateway(IDbContextFactory<ReadDbContext> dbFactory) : INestedReadGateway
{
    public async Task<AccountView?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);

        return account is null
            ? null
            : new AccountView(account.Id, account.Name, account.Type, account.Currency, account.Balance);
    }

    public async Task<IReadOnlyList<EntryView>> GetEntriesAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var entries = await db.Entries
            .AsNoTracking()
            .Where(entry => entry.TransactionId == transactionId)
            .ToListAsync(cancellationToken);

        return entries
            .Select(entry => new EntryView(entry.Id, entry.TransactionId, entry.AccountId, entry.Amount, entry.Direction))
            .ToList();
    }
}