using Ledger.ReadModel.Accounts;
using Ledger.ReadModel.Transactions;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>GraphQL projection of an account. The API surface is deliberately separate from the
/// EF projection records: the schema in <c>docs/spec.md</c> §4 is what the client sees.</summary>
public sealed record AccountView(Guid Id, string Name, AccountType Type, string Currency, decimal Balance);

/// <summary>GraphQL projection of a transaction.</summary>
public sealed record TransactionView(Guid Id, DateTimeOffset PostedAt, string? Description, string? Reference);

/// <summary>GraphQL projection of an entry. <see cref="TransactionId"/> and <see cref="AccountId"/>
/// are used by the resolvers to batch sibling-entry and account lookups; they are not exposed
/// as schema fields.</summary>
public sealed record EntryView(Guid Id, Guid TransactionId, Guid AccountId, decimal Amount, Direction Direction);

/// <summary>The <c>Money</c> value object from the schema (spec §4).</summary>
public sealed record MoneyView(decimal Amount, string Currency)
{
    public static MoneyView? From(decimal? amount, string? currency) =>
        amount is null || string.IsNullOrEmpty(currency) ? null : new MoneyView(amount.Value, currency);
}

/// <summary>Filter input for the <c>accounts</c> field (spec §4).</summary>
public sealed record AccountFilterInput(AccountType? Type, string? NameContains);
