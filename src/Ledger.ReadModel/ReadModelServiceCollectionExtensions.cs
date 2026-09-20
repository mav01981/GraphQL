using Ledger.ReadModel;
using Ledger.ReadModel.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the projection store, its query counting interceptor and the projection source.</summary>
public static class ReadModelServiceCollectionExtensions
{
    public static IServiceCollection AddReadModel(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ReadModelOptions>(configuration.GetSection(ReadModelOptions.SectionName));

        // When hosted by .NET Aspire, AppHost injects the database as
        // `ConnectionStrings__ledger-read` (see src/Ledger.AppHost/Program.cs `.WithReference(db)`).
        // That value carries the real host/port/credentials for this run, while appsettings.json
        // only holds the local-docker default (Host=localhost:5432, ledger/ledger). Prefer the
        // injected value when present — otherwise every Aspire run tries localhost:5432 with the
        // wrong credentials and fails with "password authentication failed".
        services.PostConfigure<ReadModelOptions>(options =>
        {
            var aspireConnectionString = configuration.GetConnectionString("ledger-read");
            if (!string.IsNullOrWhiteSpace(aspireConnectionString))
            {
                options.ConnectionString = aspireConnectionString;
                options.Provider = DatabaseProvider.Postgres;
            }
        });

        services.AddSingleton<SqlQueryCounter>();
        services.AddSingleton<SqlQueryCountingInterceptor>();

        services.AddDbContextFactory<ReadDbContext>((provider, options) =>
        {
            var readModel = provider.GetRequiredService<IOptions<ReadModelOptions>>().Value;
            options.AddInterceptors(provider.GetRequiredService<SqlQueryCountingInterceptor>());

            if (readModel.Provider == DatabaseProvider.Sqlite)
            {
                // Tests register a single open in-memory SqliteConnection so that schema creation,
                // seeding and querying all share the same database.
                var sharedConnection = provider.GetService<SqliteConnection>();
                if (sharedConnection is not null)
                {
                    options.UseSqlite(sharedConnection);
                }
                else
                {
                    options.UseSqlite(readModel.ConnectionString);
                }
            }
            else
            {
                options.UseNpgsql(readModel.ConnectionString, npgsql => npgsql.EnableRetryOnFailure());
            }
        });

        return services;
    }
}