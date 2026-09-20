using Ledger.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace Ledger.GraphQL.Api.Rest;

/// <summary>
/// The REST comparison surface (plan §4/§5). These endpoints are deliberately plain: one resource per
/// URL, no embedded graphs, straight EF queries.
/// <para>
/// This exists only so the claim in <c>docs/architecture.md</c> §4 can be quantified with real
/// numbers — the same account statement costs 1 round trip through GraphQL and 1 + 1 + N + M through
/// this API. It is not the write side and it is not part of the CQRS boundary: nothing here writes.
/// </para>
/// </summary>
public static class RestReadEndpoints
{
    public static IEndpointRouteBuilder MapRestReadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest").RequireAuthorization();

        group.MapGet("/accounts", async (int? limit, IDbContextFactory<ReadDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var take = Math.Clamp(limit ?? 20, 1, 100);

            var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Name).Take(take).ToListAsync(ct);
            return Results.Ok(accounts.Select(a => new AccountResponse(a.Id, a.Name, a.Type.ToString().ToUpperInvariant(), a.Currency, a.Balance)));
        });

        group.MapGet("/accounts/{id:guid}", async (Guid id, IDbContextFactory<ReadDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

            return account is null
                ? Results.NotFound()
                : Results.Ok(new AccountResponse(account.Id, account.Name, account.Type.ToString().ToUpperInvariant(), account.Currency, account.Balance));
        });

        group.MapGet("/accounts/{id:guid}/transactions", async (Guid id, int? limit, IDbContextFactory<ReadDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var take = Math.Clamp(limit ?? 20, 1, 200);

            var transactions = await db.Transactions
                .AsNoTracking()
                .Where(t => db.Entries.Any(e => e.TransactionId == t.Id && e.AccountId == id))
                .OrderByDescending(t => t.PostedAt)
                .Take(take)
                .ToListAsync(ct);

            return Results.Ok(transactions.Select(t => new TransactionResponse(t.Id, t.PostedAt, t.Description, t.Reference)));
        });

        group.MapGet("/transactions/{id:guid}/entries", async (Guid id, IDbContextFactory<ReadDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var entries = await db.Entries.AsNoTracking().Where(e => e.TransactionId == id).ToListAsync(ct);

            // A plain REST resource cannot answer "who was the counterparty" without either a join or
            // denormalized columns; here the id is resolved per transaction and the client still has to
            // fetch the account itself.
            var counterpartyIds = await ResolveCounterpartyIdsAsync(db, id, ct);

            return Results.Ok(entries.Select(e => new EntryResponse(
                e.Id,
                e.TransactionId,
                e.AccountId,
                e.Amount,
                e.Direction.ToString().ToUpperInvariant(),
                counterpartyIds.GetValueOrDefault(e.Id))));
        });

        return app;
    }

    private static async Task<Dictionary<Guid, Guid?>> ResolveCounterpartyIdsAsync(
        ReadDbContext db,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var entries = await db.Entries.AsNoTracking().Where(e => e.TransactionId == transactionId).ToListAsync(cancellationToken);
        var result = new Dictionary<Guid, Guid?>();

        foreach (var entry in entries)
        {
            var counterparty = entries
                .Where(other => other.Id != entry.Id && other.Direction != entry.Direction && other.AccountId != entry.AccountId)
                .OrderByDescending(other => other.Amount)
                .FirstOrDefault();

            result[entry.Id] = counterparty?.AccountId;
        }

        return result;
    }

    public sealed record AccountResponse(Guid Id, string Name, string Type, string Currency, decimal Balance);

    public sealed record TransactionResponse(Guid Id, DateTimeOffset PostedAt, string? Description, string? Reference);

    public sealed record EntryResponse(Guid Id, Guid TransactionId, Guid AccountId, decimal Amount, string Direction, Guid? CounterpartyAccountId);
}