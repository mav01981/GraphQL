namespace Ledger.GraphQL.Api.Graph;

/// <summary>
/// The production safeguards from spec §6/§7, all tunable from configuration so the tests and the
/// benchmark can drive abusive queries against the real limits.
/// </summary>
public sealed class GraphQLOptions
{
    public const string SectionName = "GraphQL";

    /// <summary>Maximum selection-set depth. Blocks the <c>entry → counterpartyAccount → transactions
    /// → entries</c> cycle from being walked indefinitely.</summary>
    public int MaxExecutionDepth { get; set; } = 8;

    /// <summary>Cost budget per request, in cost points (spec §7).</summary>
    public double MaxFieldCost { get; set; } = 1_000;

    public double MaxTypeCost { get; set; } = 500;

    /// <summary>
    /// Return exception messages and stack traces in the GraphQL response. True only in Development:
    /// a 500 that says nothing but "Unexpected Execution Error" is undebuggable, and this is the
    /// GraphQL equivalent of the ASP.NET Core developer exception page.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>Persisted-operations configuration. In production the API executes only documents
    /// that were pre-registered by <see cref="Persistence.PersistedOperationAllowlist"/>; in
    /// development ad-hoc queries stay available so the IDE keeps working.</summary>
    public PersistedOperationSettings PersistedOperations { get; set; } = new();

    public sealed class PersistedOperationSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Directory holding the allowlisted <c>*.graphql</c> documents, relative to the
        /// content root.</summary>
        public string DocumentsDirectory { get; set; } = "operations";

        /// <summary>Directory the file-system storage uses, relative to the content root.</summary>
        public string StorageDirectory { get; set; } = "operations/.store";

        /// <summary>When true, only pre-registered documents execute (spec §6).</summary>
        public bool OnlyAllowPersistedOperations { get; set; }

        /// <summary>When true the server persists any document body a client sends, which is how the
        /// allowlist gets extended (dev/CI convenience).</summary>
        public bool AllowDocumentBody { get; set; } = true;
    }
}