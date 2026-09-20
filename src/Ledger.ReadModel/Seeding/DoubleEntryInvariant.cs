using Ledger.ReadModel.Transactions;

namespace Ledger.ReadModel.Seeding;

/// <summary>Verifies the double-entry invariant the seeded data must satisfy: every transaction's
/// entries cancel out (spec §2).</summary>
public static class DoubleEntryInvariant
{
    public static void EnsureBalanced(IEnumerable<EntryRecord> entries)
    {
        foreach (var transaction in entries.GroupBy(entry => entry.TransactionId))
        {
            var signed = transaction.Sum(entry => entry.Direction == Direction.Debit ? entry.Amount : -entry.Amount);
            if (signed != 0m)
            {
                throw new InvalidOperationException(
                    $"Transaction {transaction.Key} is not balanced: entries sum to {signed:0.##}.");
            }
        }
    }
}