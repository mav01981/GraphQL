# Multi-stage build for the read-side API (spec §10). The demo signing key is supplied at runtime
# (docker-compose.yml / environment), never baked into the image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from project files first so the dependency layer caches independently of source changes.
COPY LedgerGraphQL.slnx ./
COPY src/Ledger.ReadModel/Ledger.ReadModel.csproj src/Ledger.ReadModel/
COPY src/Ledger.GraphQL.Api/Ledger.GraphQL.Api.csproj src/Ledger.GraphQL.Api/
COPY tests/Ledger.GraphQL.Tests/Ledger.GraphQL.Tests.csproj tests/Ledger.GraphQL.Tests/
COPY tools/Ledger.Benchmark/Ledger.Benchmark.csproj tools/Ledger.Benchmark/
RUN dotnet restore LedgerGraphQL.slnx

# Copy only the source needed to publish the API. The .dockerignore keeps bin/, obj/,
# artifacts/, local databases, and the pre-generated operations/.store out of the build context.
COPY src/Ledger.ReadModel/ src/Ledger.ReadModel/
COPY src/Ledger.GraphQL.Api/ src/Ledger.GraphQL.Api/

# Publish the API in Release and stage the output for the runtime stage.
RUN dotnet publish src/Ledger.GraphQL.Api/Ledger.GraphQL.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ledger.GraphQL.Api.dll"]
