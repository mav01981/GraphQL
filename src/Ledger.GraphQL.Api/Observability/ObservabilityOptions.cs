namespace Ledger.GraphQL.Api.Observability;

/// <summary>Observability switches (section <c>Observability</c>).</summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Expose the per-request SQL statement count as the <c>x-sql-queries</c> header and log
    /// it. Enabled in development and in tests — it is the evidence behind ADR-002, not a
    /// production feature.</summary>
    public bool ExposeSqlQueryCount { get; set; }
}