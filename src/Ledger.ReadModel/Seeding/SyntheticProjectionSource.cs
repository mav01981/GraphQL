using Ledger.ReadModel.Accounts;
using Ledger.ReadModel.Transactions;

namespace Ledger.ReadModel.Seeding;

/// <summary>
/// Deterministic synthetic projection source (spec §2). Given the same <see cref="SeedOptions"/> it
/// always produces the same accounts, transactions and entries, and every transaction it emits is
/// balanced — a debit leg funded by one or two credit legs in the same currency.
/// </summary>
public sealed class SyntheticProjectionSource : IProjectionSource
{
    private static readonly string[] Currencies = ["USD", "EUR", "GBP"];

    private static readonly string[] NamePrefixes =
    [
        "Cash", "Client Funds", "Settlement", "Operating", "Reserve",
        "Payroll", "Merchant", "Treasury", "Fee", "Interest",
    ];

    private static readonly string[] NameSuffixes = ["Clearing", "Wallet", "Ledger", "Account", "Pool", "Suspense"];

    private static readonly string[] Descriptions =
    [
        "Card payment", "Payroll run", "FX conversion", "Supplier invoice", "Wire transfer",
        "Refund issued", "Merchant settlement", "Interest accrual", "Fee charge", "Internal transfer",
    ];

    public Task<ProjectionDataSet> GenerateAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var random = new Random(options.RandomSeed);
        var accounts = CreateAccounts(options, random);
        var (transactions, entries, balances) = CreateTransactions(options, random, accounts);

        foreach (var account in accounts)
        {
            account.Balance = Round(balances.GetValueOrDefault(account.Id));
        }

        var dataSet = new ProjectionDataSet(accounts, transactions, entries);
        DoubleEntryInvariant.EnsureBalanced(dataSet.Entries);

        return Task.FromResult(dataSet);
    }

    private static List<AccountRecord> CreateAccounts(SeedOptions options, Random random)
    {
        var accounts = new List<AccountRecord>(options.Accounts);

        for (var index = 0; index < options.Accounts; index++)
        {
            var prefix = NamePrefixes[index % NamePrefixes.Length];
            var suffix = NameSuffixes[index / NamePrefixes.Length % NameSuffixes.Length];

            accounts.Add(new AccountRecord
            {
                Id = NextGuid(random),
                Name = $"{prefix} {suffix} {index:D4}",
                Type = ResolveType(index),
                Currency = Currencies[index % Currencies.Length],
                Balance = 0m,
                CreatedAt = options.PostedAtAnchor.AddDays(-(400 + index % 90)),
            });
        }

        return accounts;
    }

    private static (List<TransactionRecord> Transactions, List<EntryRecord> Entries, Dictionary<Guid, decimal> Balances)
        CreateTransactions(SeedOptions options, Random random, List<AccountRecord> accounts)
    {
        var transactions = new List<TransactionRecord>(options.Transactions);
        var entries = new List<EntryRecord>(options.Transactions * 2 + options.Transactions / 4);
        var balances = new Dictionary<Guid, decimal>(accounts.Count);
        var accountsByCurrency = accounts
            .GroupBy(account => account.Currency)
            .ToDictionary(group => group.Key, group => group.ToArray());

        for (var index = 0; index < options.Transactions; index++)
        {
            var currency = Currencies[PickCurrency(random)];
            var candidates = accountsByCurrency[currency];
            var transactionId = NextGuid(random);
            var postedAt = options.PostedAtAnchor.AddMinutes(-index * 26);

            var debit = candidates[random.Next(candidates.Length)];
            var credit = PickDistinct(random, candidates, debit, null);

            transactions.Add(new TransactionRecord
            {
                Id = transactionId,
                PostedAt = postedAt,
                Description = Descriptions[random.Next(Descriptions.Length)],
                Reference = $"REF-{postedAt:yyyyMM}-{index:D6}",
            });

            var amount = NextAmount(random);

            if (index % 4 == 3 && candidates.Length > 2)
            {
                // Split transaction: one debit leg funded by two credit legs (60/40). This keeps the
                // graph shape realistic — an entry can have more than one counterparty.
                var third = PickDistinct(random, candidates, debit, credit);
                var firstCredit = Round(amount * 0.6m);

                AddEntry(random, entries, balances, transactionId, debit, Direction.Debit, amount);
                AddEntry(random, entries, balances, transactionId, credit, Direction.Credit, firstCredit);
                AddEntry(random, entries, balances, transactionId, third, Direction.Credit, amount - firstCredit);
            }
            else
            {
                AddEntry(random, entries, balances, transactionId, debit, Direction.Debit, amount);
                AddEntry(random, entries, balances, transactionId, credit, Direction.Credit, amount);
            }
        }

        return (transactions, entries, balances);
    }

    private static void AddEntry(
        Random random,
        List<EntryRecord> entries,
        Dictionary<Guid, decimal> balances,
        Guid transactionId,
        AccountRecord account,
        Direction direction,
        decimal amount)
    {
        entries.Add(new EntryRecord
        {
            Id = NextGuid(random),
            TransactionId = transactionId,
            AccountId = account.Id,
            Amount = amount,
            Direction = direction,
        });

        var signed = direction == Direction.Debit ? amount : -amount;
        var movement = account.Type is AccountType.Asset or AccountType.Expense ? signed : -signed;
        balances[account.Id] = balances.GetValueOrDefault(account.Id) + movement;
    }

    private static AccountRecord PickDistinct(Random random, AccountRecord[] candidates, AccountRecord first, AccountRecord? second)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = candidates[random.Next(candidates.Length)];
            if (candidate.Id != first.Id && candidate.Id != second?.Id)
            {
                return candidate;
            }
        }

        return candidates[random.Next(candidates.Length)];
    }

    private static AccountType ResolveType(int index) => (index % 10) switch
    {
        0 => AccountType.Equity,
        1 or 2 => AccountType.Revenue,
        3 or 4 => AccountType.Expense,
        5 or 6 => AccountType.Liability,
        _ => AccountType.Asset,
    };

    private static int PickCurrency(Random random)
    {
        var roll = random.Next(100);
        return roll < 60 ? 0 : roll < 85 ? 1 : 2;
    }

    private static decimal NextAmount(Random random) => Round(random.Next(5_000, 2_500_000) / 100m);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static Guid NextGuid(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);

        // RFC 4122 version 4 markers, so seeded ids look like ids a real projector would emit.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}