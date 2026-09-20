# Ledger GraphQL — CQRS read side, deliberately nested

A portfolio demo of GraphQL used **deliberately** as the query-side surface of a CQRS system. The
write side stays out of scope (commands go through REST/gRPC + MediatR in the companion ledger
demo); this repo builds the read model and its GraphQL query layer on top of the projections.

The domain is a double-entry ledger — `Account → Transaction → Entry → CounterpartyAccount` —
chosen because it is **graph-shaped, not table-shaped**: an account has transactions, transactions
have entries, and every entry points at another account. That shape is what makes the
GraphQL-vs-REST comparison concrete instead of theoretical.

- **Stack:** .NET 10, HotChocolate, EF Core (Postgres; SQLite for zero-dependency runs), xUnit.
- **Docs:** [plan](docs/plan.md) · [architecture](docs/architecture.md) · [spec](docs/spec.md) · [ADRs](docs/adr/)

## The point in one query

A client that wants an account statement — balances, the 20 most recent transactions, every entry,
and who was on the other side of each entry — sends **one** request:

```graphql
query AccountStatement($id: ID!, $first: Int = 20) {
  account(id: $id) {
    name
    balance { amount currency }
    transactions(first: $first) {
      edges {
        node {
          postedAt
          description
          entries {
            amount { amount currency }
            direction
            counterpartyAccount { name }
          }
        }
      }
      pageInfo { hasNextPage endCursor }
    }
  }
}
```

The same view through plain REST resources costs `1 + 1 + 20 + ~M` round trips — account,
transactions, then entries **per transaction**, then the counterparty account **per entry** —
because each request's output is the next request's input and nothing can be skipped. The measured
comparison lives in [`docs/benchmark-results.md`](docs/benchmark-results.md), produced by
[`tools/Ledger.Benchmark`](tools/Ledger.Benchmark).

## What the demo demonstrates

| Concern | How it's shown | Evidence |
|---|---|---|
| Nested reads in one round trip | `AccountStatement` query | benchmark results doc |
| N+1 solved server-side | GreenDonut DataLoaders (`AccountByIdDataLoader`, `EntriesByTransactionDataLoader`) | `DataLoaderBatchingTests`, `x-sql-queries` header: **4 SQL statements** for the full statement |
| Production-safe surface | Max depth 8, cost budget 1000, persisted-operation allowlist | `QueryLimitsTests` — abusive queries rejected with `MAX_DEPTH_EXCEEDED` / `COST_LIMIT_EXCEEDED` |
| Field-level authorization | `@authorize` on `balance`, `amount`, `counterpartyAccount`; cumulative roles viewer → accountant → auditor | `FieldAuthorizationTests` — partial results with `AUTH_NOT_AUTHORIZED`, not all-or-nothing |
| Honest REST comparison | `/rest/*` read endpoints exist only to be measured against | benchmark harness |
| Observability | OpenTelemetry traces, per-request SQL count and query cost logging | `SqlQueryCountMiddleware`, `CostLoggingDiagnosticEventListener` |
| Self-documenting API | Nitro IDE (Banana Cake Pop successor) on a dev-only route | `GET /graphql/ui`, see [ADR-005](docs/adr/ADR-005-nitro-graphql-api-docs.md) |

The field-level auth behavior is a deliberate talking point: a `viewer` token querying the full
statement still gets a `200` with names, dates and directions — `balance`, `amount` and
`counterpartyAccount` come back `null` with per-field errors (try it below). A REST endpoint
would have to answer `403` for the whole request.

## Quick start (no Docker)

```bash
dotnet run --project src/Ledger.GraphQL.Api --launch-profile "Ledger.Api (sqlite)"
```

First run seeds a deterministic fixture (~1,000 accounts / 20,000 transactions, fixed RNG seed) into
SQLite and serves:

- GraphQL API: `http://localhost:5080/graphql`
- GraphQL IDE (Nitro): `http://localhost:5080/graphql/ui` — see “Exploring the API with Nitro” below
- Health: `http://localhost:5080/health`
- Dev token mint (Development only): `GET /dev/token/{viewer|accountant|auditor}`

### Try the three roles on the same query

The three demo JWTs are checked into
[`appsettings.Development.json`](src/Ledger.GraphQL.Api/appsettings.Development.json) (signed with
the checked-in demo key, valid until end of 2036 — **never reuse that key outside this demo**).
Grab one and send the statement query above:

```bash
TOKEN=$(curl -s http://localhost:5080/dev/token/auditor | jq -r .accessToken)

curl -s http://localhost:5080/graphql \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"query":"query($id: ID!){ account(id: $id){ name balance { amount } transactions(first: 5){ edges { node { entries { amount { amount } counterpartyAccount { name } } } } } } }","variables":{"id":"<some-account-id>"}}'
```

Swap `auditor` for `viewer` and watch `balance`/`amount`/`counterpartyAccount` null out with
`AUTH_NOT_AUTHORIZED` while the rest of the response stays intact. Persisted documents live in
[`operations/`](src/Ledger.GraphQL.Api/operations) (`account-statement`, `account-balances`) and are
published at startup.

## Quick start (Docker)

```bash
docker compose up --build
# API on http://localhost:5080 — Postgres for the projection store, API seeds itself.
```

## Measuring the GraphQL-vs-REST claim

```bash
# with the API running (Development exposes /dev/token and x-sql-queries):
dotnet run --project tools/Ledger.Benchmark -- \
  --base-url http://localhost:5080 --iterations 30 --output docs/benchmark-results.md
```

The harness finds the busiest account, then fetches the identical statement through GraphQL (1
request) and through the REST walk (account → transactions → entries ×N → counterparties ×M),
reporting round trips, payload bytes, p50/p95 and the server's SQL statement count.

## Exploring the API with Nitro (Banana Cake Pop successor)

HotChocolate 16 ships its GraphQL IDE — **Nitro**, the renamed Banana Cake Pop — inside the
`HotChocolate.AspNetCore` package, so there is nothing extra to install. In Development the
API serves it on a separate anonymous path (the API itself stays `RequireAuthorization()`):

- GraphQL IDE: `http://localhost:5080/graphql/ui`
- GraphQL API: `http://localhost:5080/graphql` (JWT required)
- Raw schema (SDL): `http://localhost:5080/graphql/schema.graphql` (same auth as the API)

Steps:

1. Run the API, then open `http://localhost:5080/graphql/ui` in a browser.
2. Mint a role token: `GET http://localhost:5080/dev/token/{viewer|accountant|auditor}`
   (or copy one from `Jwt:DemoTokens` in
   [`appsettings.Development.json`](src/Ledger.GraphQL.Api/appsettings.Development.json)).
3. In Nitro, open Connection Settings → HTTP Headers and add
   `Authorization: Bearer <accessToken>`.
4. Paste the `AccountStatement` query from the top of this README (or open
   [`operations/account-statement.graphql`](src/Ledger.GraphQL.Api/operations/account-statement.graphql))
   and run it. Use the Schema Reference pane for field/enum/`@authorize` docs.

Repeat step 4 with the `viewer` token: `balance`, `amount` and `counterpartyAccount` come back
`null` with `AUTH_NOT_AUTHORIZED` while the rest of the statement succeeds — the same graceful
degradation as the `curl` example above.

Notes:

- The IDE route only exists in Development; there is no docs UI in production.
- If `GraphQL:PersistedOperations:OnlyAllowPersistedOperations` is turned on, ad-hoc IDE
  queries are rejected — leave `AllowDocumentBody: true` while exploring.
- Offline/air-gapped? Switch Nitro to the embedded bundle:
  `WithOptions(o => o.ServeMode = ServeMode.Embedded)` (default `Latest` loads from a CDN).

Rationale lives in [ADR-005](docs/adr/ADR-005-nitro-graphql-api-docs.md).

## Repository layout

```
src/Ledger.AppHost           .NET Aspire App Host for local development (orchestrates Postgres + API with dashboard)
src/Ledger.GraphQL.Api       HotChocolate schema, resolvers, DataLoaders, auth, persisted ops, REST twin
src/Ledger.ReadModel         Projection store (EF Core), deterministic seeder, SQL query counting
tests/Ledger.GraphQL.Tests   Batching, authorization, limits and smoke tests (in-memory SQLite)
tools/Ledger.Benchmark       GraphQL-vs-REST harness (round trips, bytes, latency, SQL count)
docs/                        plan, architecture, spec, ADRs, benchmark results
```

## Local development with .NET Aspire

Run the entire stack locally with the Aspire dashboard (service discovery, logs, traces):

```bash
dotnet run --project src/Ledger.AppHost
```

This starts:
- **Postgres** container for the read model
- **GraphQL API** with configuration automatically injected from the App Host
- **Aspire Dashboard** at `http://localhost:15223` for monitoring

The API exposes:
- GraphQL endpoint: `http://localhost:5080/graphql`
- Health check: `http://localhost:5080/health`
- Dev token mint: `GET /dev/token/{viewer|accountant|auditor}`

## Running the tests

```bash
dotnet test
```

Tests host the real `Program` via `WebApplicationFactory` against an in-memory SQLite database —
the same middleware, policies and schema the API runs with.

## Scope and honest limits

Deliberately out of scope: real event consumption from the write side (`IProjectionSource` is the
swap-in point), subscriptions, federation (see [ADR-004](docs/adr/ADR-004-no-federation.md)), and
load-tested perf numbers (targets in the [spec](docs/spec.md) are indicative only). Known
trade-offs — caching, team ramp-up, schema discipline — are listed in
[architecture.md §9](docs/architecture.md).
