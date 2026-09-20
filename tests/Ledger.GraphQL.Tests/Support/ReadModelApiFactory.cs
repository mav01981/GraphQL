using Ledger.GraphQL.Api.Security;
using Ledger.ReadModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ledger.GraphQL.Tests.Support;

/// <summary>
/// Hosts the real application for tests. The projection store is a shared in-memory SQLite database
/// seeded with a small deterministic fixture set (spec §9), so these tests run without Docker. The
/// Postgres path is exercised by <c>docker-compose</c> and the benchmark run instead of in-process —
/// a Testcontainers fixture would need a Docker daemon to be available to every contributor and CI
/// agent, which is a worse default for a demo repo than an honest statement of what was verified.
/// </summary>
public sealed class ReadModelApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, string?> _settings;
    private readonly CapturingLoggerProvider _loggerProvider = new();

    public ReadModelApiFactory(int accounts = 24, int transactions = 40, bool useDataLoaders = true)
    {
        _connection = new SqliteConnection($"Data Source=file:ledger-tests-{Guid.NewGuid():N}?mode=memory&cache=shared");
        _connection.Open();

        _settings = new Dictionary<string, string?>
        {
            ["ReadModel:Provider"] = nameof(DatabaseProvider.Sqlite),
            ["ReadModel:UseDataLoaders"] = useDataLoaders.ToString(),
            ["Seed:RunOnStartup"] = "true",
            ["Seed:Reset"] = "true",
            ["Seed:Accounts"] = accounts.ToString(),
            ["Seed:Transactions"] = transactions.ToString(),
            ["GraphQL:PersistedOperations:Enabled"] = "false",
            ["Observability:ExposeSqlQueryCount"] = "true",
            ["Jwt:SigningKey"] = "ledger-demo-signing-key-do-not-use-outside-this-demo-0123456789",
        };
    }

    /// <summary>Per-request SQL statement counts, keyed by the log message (see the batching tests).</summary>
    public CapturingLoggerProvider Logs => _loggerProvider;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(_settings);
        });

        builder.ConfigureServices(services =>
        {
            // One open in-memory connection shared by schema creation, seeding and every query.
            services.AddSingleton(_connection);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(_loggerProvider));
    }

    public string CreateToken(string role) =>
        Services.GetRequiredService<DevTokenIssuer>().Issue(role);

    /// <summary>Client with an <c>auditor</c> bearer token — the role that may see every protected
    /// field, which keeps these tests focused on the query pipeline rather than authorization.</summary>
    public HttpClient CreateAuthenticatedClient(string role = AuthPolicies.Auditor)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateToken(role));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}