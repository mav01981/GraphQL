# Benchmark results - GraphQL vs REST on the same statement

> Measured 30 executions per leg against `http://localhost:5080` (seed scale: appsettings defaults). Subject: account adf4c55e... (Client Funds Ledger 0021) - 20 transactions, 51 entries nested in the statement. All legs fetched the same data: account, its 20 most recent transactions, every entry, and every counterparty account.

| Surface | HTTP round trips | Payload (median) | p50 | p95 | SQL statements |
|---|---:|---:|---:|---:|---:|
| GraphQL (1 AccountStatement query) | 1 | 8,184 B | 8.7 ms | 10.0 ms | 6 |
| REST (account + transactions + entries + counterparties) | 41 | 18,410 B | 81.5 ms | 97.7 ms | n/a |

The REST leg is not a strawman: each of its requests is the *minimum* a client can make against plain resources, because the output of each step is the input of the next. GraphQL collapses all of it into one round trip and one nested payload.

Reproduce with:

```bash
# 1. start the API (seeds itself on first run)
dotnet run --project src/Ledger.GraphQL.Api --launch-profile "Ledger.Api (sqlite)"

# 2. run the harness (Development exposes the dev token endpoint and x-sql-queries)
dotnet run --project tools/Ledger.Benchmark -- --base-url http://localhost:5080 --iterations 30 --output docs/benchmark-results.md
```
