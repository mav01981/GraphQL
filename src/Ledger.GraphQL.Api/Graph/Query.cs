using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using HotChocolate.Authorization;
using Ledger.ReadModel;
using Ledger.ReadModel.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>Root query fields (<c>account</c>, <c>accounts</c>, <c>transaction</c>).</summary>
public sealed class Query
{
    public Task<AccountView?> GetAccountAsync(
        Guid id,
        INestedReadGateway gateway,
        CancellationToken cancellationToken) =>
        gateway.GetAccountAsync(id, cancellationToken);

    [UsePaging(DefaultPageSize = 25, MaxPageSize = 100, IncludeTotalCount = true)]
    // Cost model (spec §7 / ADR-003): the account list is priced per requested page size; `edges`
    // is the sized field of the connection.
    [Cost(CostWeights.Account)]
    [ListSize(AssumedSize = 25, SlicingArguments = ["first"], SizedFields = ["edges"])]
    public async Task<IReadOnlyList<AccountView>> GetAccountsAsync(
        AccountFilterInput? filter,
        IDbContextFactory<ReadDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Accounts.AsNoTracking();

        if (filter?.Type is { } type)
        {
            query = query.Where(account => account.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(filter?.NameContains))
        {
            var pattern = $"%{filter.NameContains}%";
            query = query.Where(account => EF.Functions.Like(account.Name, pattern));
        }

        var accounts = await query
            .OrderBy(account => account.Name)
            .ToListAsync(cancellationToken);

        return accounts
            .Select(account => new AccountView(account.Id, account.Name, account.Type, account.Currency, account.Balance))
            .ToList();
    }

    public async Task<TransactionView?> GetTransactionAsync(
        Guid id,
        IDbContextFactory<ReadDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var transaction = await db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        // Not-found entities resolve to null rather than an error (spec §8).
        return transaction is null
            ? null
            : new TransactionView(transaction.Id, transaction.PostedAt, transaction.Description, transaction.Reference);
    }
}