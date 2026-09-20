namespace Ledger.GraphQL.Api.Security;

/// <summary>
/// Local dev token issuer settings (section <c>Jwt</c>). There is no real identity provider in the
/// demo: <see cref="DevTokenIssuer"/> mints role tokens with the symmetric key from configuration, and
/// the three demo tokens in <c>appsettings.Development.json</c> are signed with the same key.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "https://ledger-demo.local";

    public string Audience { get; set; } = "ledger-graphql-read-api";

    public string SigningKey { get; set; } = string.Empty;

    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>The three role tokens committed to <c>appsettings.Development.json</c> (spec §10).</summary>
    public DemoTokenOptions DemoTokens { get; set; } = new();

    public sealed class DemoTokenOptions
    {
        public string? Viewer { get; set; }

        public string? Accountant { get; set; }

        public string? Auditor { get; set; }
    }
}