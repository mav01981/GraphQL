namespace Ledger.ReadModel;

/// <summary>Projection store provider. Postgres is the documented default (spec §3); SQLite exists
/// so the demo, the tests and the benchmark can run on machines without Docker.</summary>
public enum DatabaseProvider
{
    Postgres,
    Sqlite,
}

/// <summary>Configuration for the projection store (section <c>ReadModel</c>).</summary>
public sealed class ReadModelOptions
{
    public const string SectionName = "ReadModel";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Postgres;

    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// When true the nested resolvers resolve through GreenDonut DataLoaders; when false they use the
    /// naive per-parent query implementation. The switch exists so the N+1-vs-batching claim in
    /// ADR-002 can be measured rather than asserted (see <c>docs/benchmark-results.md</c>).
    /// </summary>
    public bool UseDataLoaders { get; set; } = true;
}