using Ledger.GraphQL.Api.Graph;
using Ledger.GraphQL.Api.Observability;
using Ledger.GraphQL.Api.Persistence;
using Ledger.GraphQL.Api.Rest;
using Ledger.GraphQL.Api.Security;
using Ledger.ReadModel.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---- projection store + seeding -----------------------------------------------------------------
builder.Services.AddReadModel(builder.Configuration);
builder.Services.AddProjectionSeeding(builder.Configuration);

// ---- GraphQL, security, observability -----------------------------------------------------------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DevTokenIssuer>();
builder.Services.AddSingleton<PersistedOperationAllowlist>();
builder.Services.AddLedgerGraphQL(builder.Configuration, builder.Environment.ContentRootPath);

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured. Set it in appsettings.Development.json or via the Jwt__SigningKey environment variable.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });

// Field-level policies are the authorization model (spec §5); the request-level check is only
// "is the token valid at all".
builder.Services.AddAuthorizationBuilder().AddLedgerPolicies();

// Resolver-level tracing (spec §7). The console exporter keeps it visible in local runs; an OTLP
// exporter would be swapped in for a real deployment.
builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHotChocolateInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

// HotChocolate resolves schema-scope services (diagnostic listeners, request middleware) from its own
// container, so the host logger factory is handed over explicitly — see SchemaDiagnosticSink.
SchemaDiagnosticSink.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
app.UseSqlQueryCountLogging();
app.UseAuthentication();
app.UseAuthorization();

// ---- projection store seeding (spec §2/§10) ------------------------------------------------------
// `dotnet run --seed` populates the store and exits; `--reset` clears it first.
var seedOptions = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
if (args.Contains("--reset", StringComparer.OrdinalIgnoreCase))
{
    seedOptions = seedOptions with { Reset = true };
}

var seedOnly = args.Contains("--seed", StringComparer.OrdinalIgnoreCase);
if (seedOnly || seedOptions.RunOnStartup)
{
    var seeder = app.Services.GetRequiredService<ProjectionSeeder>();
    await seeder.SeedAsync(seedOptions, CancellationToken.None);
}

if (seedOnly)
{
    return;
}

// ---- persisted operations (spec §6) --------------------------------------------------------------
var graphqlOptions = app.Services.GetRequiredService<IOptions<GraphQLOptions>>().Value;
if (graphqlOptions.PersistedOperations.Enabled)
{
    var allowlist = app.Services.GetRequiredService<PersistedOperationAllowlist>();
    await allowlist.PublishAsync(CancellationToken.None);
}

// ---- endpoints -----------------------------------------------------------------------------------
// The token is validated once per request; every sensitive field is then gated by its own policy.
app.MapGraphQL("/graphql").RequireAuthorization();

// The REST surface exists only to quantify the GraphQL-vs-REST round-trip difference (plan §5).
app.MapRestReadEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ledger-graphql-read-api" }))
    .AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapDevTokenEndpoints();
    app.MapNitroApp("/graphql/ui").AllowAnonymous();
}

await app.RunAsync();

/// <summary>Entry point marker so the test project can host the real application.</summary>
public partial class Program;

