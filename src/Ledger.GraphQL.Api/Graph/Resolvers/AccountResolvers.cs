using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;
using HotChocolate.Authorization;
using Ledger.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// Resolvers added to the <c>Account</c> schema type.
/// </summary>
[ExtendObjectType(typeof(AccountView))]
public sealed class AccountResolvers
{
    /// <summary>
    /// Safety bound on the per-account statement window. A production statement view would page at the
    /// database with a keyset cursor; this demo bounds the preload and pages in memory (ADR-002).
    /// </summary>
    public const int MaxTransactionsPerAccount = 200;

    [UsePaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true)]
    // Cost model (spec §7 / ADR-003): the statement window is priced per requested page size, so the
    // analyser multiplies the selected subtree by `first` (capped at MaxPageSize = 100). The sized
    // field is `edges`: a connection's item list is what the slicing argument bounds.
    [Cost(CostWeights.Transaction)]
    [ListSize(AssumedSize = 20, SlicingArguments = ["first"], SizedFields = ["edges"])]
    public async Task<IReadOnlyList<TransactionView>> GetTransactionsAsync(
        [Parent] AccountView account,
        IDbContextFactory<ReadDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(transaction => db.Entries.Any(entry =>
                entry.TransactionId == transaction.Id && entry.AccountId == account.Id))
            .OrderByDescending(transaction => transaction.PostedAt)
            .Take(MaxTransactionsPerAccount)
            .ToListAsync(cancellationToken);

        return transactions
            .Select(transaction => new TransactionView(
                transaction.Id,
                transaction.PostedAt,
                transaction.Description,
                transaction.Reference))
            .ToList();
    }
}