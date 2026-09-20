using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database for the read model.
// Stable local credentials (ledger/ledger) so repeated `dotnet run` invocations reuse the
// same data volume without password-auth failures after a password rotation.
var pgUser = builder.AddParameter("postgres-user", value: "ledger");
var pgPassword = builder.AddParameter("postgres-password", value: "ledger", secret: true);
var postgres = builder.AddPostgres("postgres", userName: pgUser, password: pgPassword)
    .WithDataVolume("ledger-pgdata");

var db = postgres.AddDatabase("ledger-read", "ledger_read");

// Add the GraphQL API project (uses string path to avoid requiring generated type)
var api = builder
    .AddProject("api", "../Ledger.GraphQL.Api/Ledger.GraphQL.Api.csproj")
    .WithReference(db)
    .WithExternalHttpEndpoints()
    .WaitFor(db);

builder.Build().Run();
