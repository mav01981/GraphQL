using HotChocolate;
using HotChocolate.Types;
using Ledger.GraphQL.Api.Graph;
using Ledger.GraphQL.Api.Graph.DataLoaders;
using Ledger.GraphQL.Api.Observability;
using Ledger.ReadModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class GraphQLServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerGraphQL(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var graphql = configuration.GetSection(GraphQLOptions.SectionName).Get<GraphQLOptions>() ?? new GraphQLOptions();

        // Registered on the application container (for the host) and on the schema container (for
        // schema-scope services such as diagnostic event listeners).
        services.Configure<GraphQLOptions>(configuration.GetSection(GraphQLOptions.SectionName));

        // The batching switch from ADR-002: same schema, same resolvers, different nested read path.
        // It is deliberately resolved from the bound options at request time rather than read from
        // IConfiguration while wiring the container — configuration added after this call (test hosts,
        // environment-specific overrides) must still be able to flip it.
        services.AddScoped<DataLoaderNestedReadGateway>();
        services.AddScoped<NaiveNestedReadGateway>();
        services.AddScoped<INestedReadGateway>(provider =>
            provider.GetRequiredService<IOptions<ReadModelOptions>>().Value.UseDataLoaders
                ? provider.GetRequiredService<DataLoaderNestedReadGateway>()
                : provider.GetRequiredService<NaiveNestedReadGateway>());

        var storageDirectory = System.IO.Path.Combine(contentRootPath, graphql.PersistedOperations.StorageDirectory);

        // Always registered on the application container so the startup allowlist publisher writes
        // into the same store; the request pipeline only reads it when persisted operations are
        // enabled (and gets its own registration below — HotChocolate's schema-scope container does
        // not fall back to application services).
        services.AddFileSystemOperationDocumentStorage(storageDirectory);

        var builder = services
            .AddGraphQLServer(disableDefaultSecurity: true)
            .AddQueryType<Query>()
            .AddType<AccountObjectType>()
            .AddType<TransactionObjectType>()
            .AddType<EntryObjectType>()
            .AddType<MoneyObjectType>()
            .AddType<DirectionEnumType>()
            .AddType<AccountTypeEnumType>()
            .AddTypeExtension<AccountResolvers>()
            .AddTypeExtension<TransactionResolvers>()
            .AddTypeExtension<EntryResolvers>()
            // AddAuthorization() comes from HotChocolate.AspNetCore.Authorization: it registers the
            // DefaultAuthorizationHandler that evaluates the ASP.NET Core policies registered in the
            // host (AuthPolicies.AddLedgerPolicies). AddAuthorizationCore() alone only registers the
            // directive and leaves IAuthorizationHandler unresolved, which makes *every* request fail
            // with a 500 before any resolver runs.
            .AddAuthorization()
            .AddDataLoader<AccountByIdDataLoader>()
            .AddDataLoader<EntriesByTransactionDataLoader>()
            .AddDiagnosticEventListener<CostLoggingDiagnosticEventListener>()
            // The cost analyser is what actually computes and enforces MaxFieldCost/MaxTypeCost and
            // raises the OperationCost diagnostic event; without it the cost options are inert
            // (ADR-003). It ships in the separate HotChocolate.CostAnalysis package in HC 16.
            .AddCostAnalyzer()
            .BindRuntimeType<Guid, IdType>()
            .ModifyPagingOptions(paging =>
            {
                // RequirePagingBoundaries keeps an unbounded `accounts { ... }` from being a
                // one-request denial of service.
                paging.DefaultPageSize = 20;
                paging.MaxPageSize = 100;
                paging.RequirePagingBoundaries = true;
                paging.IncludeTotalCount = false;
            })
            .ModifyRequestOptions(execution =>
            {
                execution.PersistedOperations.OnlyAllowPersistedDocuments = graphql.PersistedOperations.OnlyAllowPersistedOperations;
                execution.PersistedOperations.AllowDocumentBody = graphql.PersistedOperations.AllowDocumentBody;
                execution.IncludeExceptionDetails = graphql.IncludeExceptionDetails;
            })
            .ModifyCostOptions(cost =>
            {
                cost.MaxFieldCost = graphql.MaxFieldCost;
                cost.MaxTypeCost = graphql.MaxTypeCost;
                cost.EnforceCostLimits = true;
            })
            .AddMaxExecutionDepthRule(graphql.MaxExecutionDepth);

        // Schema-scope services see their own container; the cost log listener needs the options there.
        builder.Services.AddSingleton(graphql);
        builder.Services.Configure<GraphQLOptions>(configuration.GetSection(GraphQLOptions.SectionName));

        if (graphql.PersistedOperations.Enabled)
        {
            builder.AddFileSystemOperationDocumentStorage(storageDirectory);
            builder.UsePersistedOperationPipeline();

            if (graphql.PersistedOperations.OnlyAllowPersistedOperations)
            {
                builder.UseOnlyPersistedOperationAllowed();
            }
        }

        return services;
    }
}