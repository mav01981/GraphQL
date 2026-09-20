using Ledger.GraphQL.Api.Security;
using Ledger.ReadModel.Accounts;
using Ledger.ReadModel.Transactions;
using HotChocolate.Authorization;
using HotChocolate.Types;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// Schema type for <see cref="AccountView"/>. <c>balance</c> is the first of the three
/// field-level authorization points in the demo (spec §5).
/// </summary>
public sealed class AccountObjectType : ObjectType<AccountView>
{
    protected override void Configure(IObjectTypeDescriptor<AccountView> descriptor)
    {
        descriptor.Name("Account");

        descriptor.Field(account => account.Id).Type<NonNullType<IdType>>();
        descriptor.Field(account => account.Name).Type<NonNullType<StringType>>();
        descriptor.Field(account => account.Type).Type<NonNullType<AccountTypeEnumType>>();
        descriptor.Field(account => account.Currency).Type<NonNullType<StringType>>();

        descriptor.Field(account => account.Balance)
            .Type<MoneyObjectType>()
            .Description("Ledger balance. Requires the ViewBalance policy (accountant or auditor).")
            .Resolve(context => MoneyView.From(
                context.Parent<AccountView>().Balance,
                context.Parent<AccountView>().Currency))
            .Authorize(AuthPolicies.ViewBalance, ApplyPolicy.BeforeResolver);
    }
}

/// <summary>Schema type for <see cref="TransactionView"/>. <c>entries</c> is added by
/// <see cref="Resolvers.TransactionResolvers"/> through a DataLoader.</summary>
public sealed class TransactionObjectType : ObjectType<TransactionView>
{
    protected override void Configure(IObjectTypeDescriptor<TransactionView> descriptor)
    {
        descriptor.Name("Transaction");

        descriptor.Field(transaction => transaction.Id).Type<NonNullType<IdType>>();
        descriptor.Field(transaction => transaction.PostedAt).Type<NonNullType<DateTimeType>>();
        descriptor.Field(transaction => transaction.Description).Type<StringType>();
        descriptor.Field(transaction => transaction.Reference).Type<StringType>();
    }
}

/// <summary>
/// Schema type for <see cref="EntryView"/>. Both <c>amount</c> and <c>counterpartyAccount</c> are
/// policy-gated, and both are nullable on purpose: a non-null field cannot be withheld per field
/// without nulling the whole parent object, which would defeat the graceful-degradation behaviour
/// described in spec §5 (see ADR-004).
/// </summary>
public sealed class EntryObjectType : ObjectType<EntryView>
{
    protected override void Configure(IObjectTypeDescriptor<EntryView> descriptor)
    {
        descriptor.Name("Entry");

        descriptor.Field(entry => entry.Id).Type<NonNullType<IdType>>();

        // Resolved by EntryResolvers (through the nested read gateway) so the currency can be attached
        // and the ViewAmount policy enforced per field.
        descriptor.Field(entry => entry.Amount).Ignore();

        descriptor.Field(entry => entry.Direction).Type<NonNullType<DirectionEnumType>>();
    }
}

/// <summary>Schema type for <see cref="MoneyView"/>.</summary>
public sealed class MoneyObjectType : ObjectType<MoneyView>
{
    protected override void Configure(IObjectTypeDescriptor<MoneyView> descriptor)
    {
        descriptor.Name("Money");
        descriptor.Field(money => money.Amount).Type<NonNullType<DecimalType>>();
        descriptor.Field(money => money.Currency).Type<NonNullType<StringType>>();
    }
}

/// <summary><c>enum Direction { DEBIT CREDIT }</c></summary>
public sealed class DirectionEnumType : EnumType<Direction>
{
    protected override void Configure(IEnumTypeDescriptor<Direction> descriptor)
    {
        descriptor.Name("Direction");
        descriptor.Value(Direction.Debit).Name("DEBIT");
        descriptor.Value(Direction.Credit).Name("CREDIT");
    }
}

/// <summary><c>enum AccountType { ASSET LIABILITY EQUITY REVENUE EXPENSE }</c></summary>
public sealed class AccountTypeEnumType : EnumType<AccountType>
{
    protected override void Configure(IEnumTypeDescriptor<AccountType> descriptor)
    {
        descriptor.Name("AccountType");
        descriptor.Value(AccountType.Asset).Name("ASSET");
        descriptor.Value(AccountType.Liability).Name("LIABILITY");
        descriptor.Value(AccountType.Equity).Name("EQUITY");
        descriptor.Value(AccountType.Revenue).Name("REVENUE");
        descriptor.Value(AccountType.Expense).Name("EXPENSE");
    }
}
