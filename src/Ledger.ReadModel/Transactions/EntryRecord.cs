namespace Ledger.ReadModel.Transactions;

/// <summary>
/// Read-model projection of a single ledger entry. The counterparty account of an entry is
/// intentionally NOT denormalized here: it is a sibling entry of the same transaction, so it can be
/// derived in one batched load instead of duplicating data into every row (see ADR-002).
/// </summary>
public sealed class EntryRecord
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }

    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public Direction Direction { get; set; }
}