using HotChocolate.Types;
using Ledger.GraphQL.Api.Security;
using Ledger.ReadModel.Accounts;

namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// The cost weights of the schema (spec §7: the AccountStatement view has to sit "comfortably under
/// budget" while abusive queries don't). Weights are centralised here so ADR-003's budget can be
/// reasoned about in one place:
/// <list type="bullet">
///   <item>account = 20, transaction = 30 (the statement window), entry = 8, counterparty = 10</item>
///   <item>everything else (scalars, enums, Money) = the analyser default (1)</item>
/// </list>
/// The measured baseline: a full statement with 20 transactions × ~2 entries ≈ 912 cost points,
/// against a MaxFieldCost budget of 1,000. The same query at <c>first: 100</c>, or the benchmark's
/// wide <c>accounts(first: 100)</c> walk, prices far over budget and is rejected.
/// </summary>
public static class CostWeights
{
    public const double Account = 20;
    public const double Transaction = 30;
    public const double Entry = 8;
    public const double Counterparty = 10;
}
