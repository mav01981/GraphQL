namespace Ledger.ReadModel.Transactions;

/// <summary>Read-model projection of a balanced transaction (a set of entries that sum to zero).</summary>
public sealed class TransactionRecord
{
    public Guid Id { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    public string? Description { get; set; }

    public string? Reference { get; set; }
}