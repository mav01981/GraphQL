using Ledger.ReadModel.Accounts;
using Ledger.ReadModel.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ledger.ReadModel;

/// <summary>
/// EF Core context over the projection store. The table/column names mirror the read-model schema
/// in <c>docs/spec.md</c> §3 exactly, so the SQL emitted by the DataLoader batching is recognisable
/// in query logs.
/// </summary>
public sealed class ReadDbContext(DbContextOptions<ReadDbContext> options) : DbContext(options)
{
    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    public DbSet<EntryRecord> Entries => Set<EntryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var accounts = modelBuilder.Entity<AccountRecord>();
        accounts.ToTable("accounts");
        accounts.HasKey(x => x.Id);
        accounts.Property(x => x.Id).HasColumnName("id");
        accounts.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        accounts.Property(x => x.Type)
            .HasColumnName("type")
            .HasMaxLength(16)
            .IsRequired()
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<AccountType>(value, ignoreCase: true));
        accounts.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        accounts.Property(x => x.Balance).HasColumnName("balance").HasPrecision(18, 2).IsRequired();
        accounts.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        var transactions = modelBuilder.Entity<TransactionRecord>();
        transactions.ToTable("transactions");
        transactions.HasKey(x => x.Id);
        transactions.Property(x => x.Id).HasColumnName("id");
        transactions.Property(x => x.PostedAt).HasColumnName("posted_at").IsRequired();
        transactions.Property(x => x.Description).HasColumnName("description").HasMaxLength(400);
        transactions.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(64);
        transactions.HasIndex(x => x.PostedAt).HasDatabaseName("ix_transactions_posted_at");

        var entries = modelBuilder.Entity<EntryRecord>();
        entries.ToTable("entries");
        entries.HasKey(x => x.Id);
        entries.Property(x => x.Id).HasColumnName("id");
        entries.Property(x => x.TransactionId).HasColumnName("transaction_id").IsRequired();
        entries.Property(x => x.AccountId).HasColumnName("account_id").IsRequired();
        entries.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2).IsRequired();
        entries.Property(x => x.Direction)
            .HasColumnName("direction")
            .HasMaxLength(6)
            .IsRequired()
            .HasConversion(
                value => value.ToString().ToUpperInvariant(),
                value => Enum.Parse<Direction>(value, ignoreCase: true));
        entries.HasIndex(x => x.AccountId).HasDatabaseName("ix_entries_account_id");
        entries.HasIndex(x => x.TransactionId).HasDatabaseName("ix_entries_transaction_id");

        var accountsByCurrency = accounts.HasIndex(x => x.Currency).HasDatabaseName("ix_accounts_currency");
        accountsByCurrency.IsUnique(false);

        if (Database.IsSqlite())
        {
            // SQLite stores DateTimeOffset as text with an offset and refuses to compare it, so any
            // `ORDER BY posted_at` fails with "SQLite does not support expressions of type
            // 'DateTimeOffset' in ORDER BY clauses". Projections are written in UTC, so on SQLite the
            // columns are stored as UTC DateTime and read back as DateTimeOffset with a zero offset.
            // Postgres keeps its native timestamptz mapping (spec §3) — this is a provider detail, not
            // a change to the read model.
            var utcDateTime = new ValueConverter<DateTimeOffset, DateTime>(
                value => value.ToUniversalTime().UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

            transactions.Property(x => x.PostedAt).HasConversion(utcDateTime);
            accounts.Property(x => x.CreatedAt).HasConversion(utcDateTime);
        }
    }
}