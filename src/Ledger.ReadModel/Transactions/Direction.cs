namespace Ledger.ReadModel.Transactions;

/// <summary>Sign of a ledger entry (spec §3: stored as text).</summary>
public enum Direction
{
    Debit,
    Credit,
}