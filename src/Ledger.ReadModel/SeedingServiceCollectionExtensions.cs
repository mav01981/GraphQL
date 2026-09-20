using Ledger.ReadModel.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class SeedingServiceCollectionExtensions
{
    public static IServiceCollection AddProjectionSeeding(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddSingleton<IProjectionSource, SyntheticProjectionSource>();
        services.AddSingleton<ProjectionSeeder>();
        return services;
    }
}