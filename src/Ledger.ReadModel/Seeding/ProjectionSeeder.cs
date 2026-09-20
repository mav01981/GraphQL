using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ledger.ReadModel.Seeding;

public enum SeedOutcome
{
    Seeded,
    Skipped,
}

public sealed record SeedResult(SeedOutcome Outcome, int Accounts, int Transactions, int Entries)
{
    public override string ToString() =>
        Outcome == SeedOutcome.Skipped
            ? $"skipped (projection store already holds {Accounts:n0} accounts)"
            : $"seeded {Accounts:n0} accounts, {Transactions:n0} transactions, {Entries:n0} entries";
}

/// <summary>
/// Populates the projection store directly — no event store involved in this version (spec §2). Safe
/// to re-run: it only seeds an empty store unless <see cref="SeedOptions.Reset"/> is set.
/// </summary>
public sealed class ProjectionSeeder(
    IDbContextFactory<ReadDbContext> dbFactory,
    IProjectionSource projectionSource,
    ILogger<ProjectionSeeder> logger)
{
    private const int BatchSize = 2_000;

    public async Task<SeedResult> SeedAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var existingAccounts = await db.Accounts.CountAsync(cancellationToken);

        if (existingAccounts > 0 && !options.Reset)
        {
            var counts = await ReadCountsAsync(db, cancellationToken);
            logger.LogInformation("Projection store already populated ({Counts}); seeding skipped", counts);
            return new SeedResult(SeedOutcome.Skipped, counts.Accounts, counts.Transactions, counts.Entries);
        }

        if (existingAccounts > 0)
        {
            logger.LogInformation("Reset requested: clearing projection store");
            await db.Entries.ExecuteDeleteAsync(cancellationToken);
            await db.Transactions.ExecuteDeleteAsync(cancellationToken);
            await db.Accounts.ExecuteDeleteAsync(cancellationToken);
        }

        var data = await projectionSource.GenerateAsync(options, cancellationToken);

        await InsertAsync(db, data.Accounts, cancellationToken);
        await InsertAsync(db, data.Transactions, cancellationToken);
        await InsertAsync(db, data.Entries, cancellationToken);

        var result = new SeedResult(SeedOutcome.Seeded, data.Accounts.Count, data.Transactions.Count, data.Entries.Count);
        logger.LogInformation("Projection store {Result}", result);
        return result;
    }

    private static async Task<(int Accounts, int Transactions, int Entries)> ReadCountsAsync(
        ReadDbContext db,
        CancellationToken cancellationToken) =>
        (
            await db.Accounts.CountAsync(cancellationToken),
            await db.Transactions.CountAsync(cancellationToken),
            await db.Entries.CountAsync(cancellationToken)
        );

    private static async Task InsertAsync<TEntity>(ReadDbContext db, IReadOnlyList<TEntity> entities, CancellationToken cancellationToken)
        where TEntity : class
    {
        foreach (var batch in entities.Chunk(BatchSize))
        {
            db.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }
}