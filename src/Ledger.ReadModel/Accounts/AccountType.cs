namespace Ledger.ReadModel.Accounts;

/// <summary>Accounting classification of a ledger account (spec §3: stored as text).</summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
}