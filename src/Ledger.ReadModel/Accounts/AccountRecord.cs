namespace Ledger.ReadModel.Accounts;

/// <summary>
/// Read-model projection of an account. This is deliberately a projection record and not the
/// write-side aggregate: the GraphQL layer only ever reads it (see ADR-001).
/// </summary>
public sealed class AccountRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Denormalized balance, maintained at projection time (spec §3).</summary>
    public decimal Balance { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}