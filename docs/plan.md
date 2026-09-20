# Project Plan — CQRS Read-Side via GraphQL (.NET 10)

## 1. Overview

A portfolio demo showing GraphQL used deliberately as the **query-side surface** of a CQRS system, rather than as a general-purpose API style. The write side stays untouched — commands go through a REST/gRPC endpoint via MediatR, exactly as in the [ledger CQRS demo](#) — and this project builds the **read model + GraphQL query layer** on top of the resulting projections.

Domain used to demonstrate it: a double-entry ledger/wallet service (`Account → Transaction → Entry → CounterpartyAccount`). This domain is chosen specifically because it is **graph-shaped, not table-shaped** — an account naturally has many transactions, each transaction has multiple entries, and each entry references another account. That shape is what makes the GraphQL-vs-REST comparison concrete rather than theoretical.

The goal isn't "I used GraphQL." It's demonstrating the architectural reasoning a tech lead is expected to have: *when* GraphQL earns its complexity on the read side, what it costs, and how to keep it production-safe.

## 2. Goals

- Show a CQRS read-side implemented as a GraphQL query layer over projections, cleanly separated from the command/write side.
- Demonstrate the **nested-data** win concretely: one client query replacing several dependent REST round trips, with the N+1 problem solved server-side via DataLoader rather than pushed onto the client.
- Include the production safeguards that separate a "GraphQL toy" from something you'd actually ship: query complexity/depth limiting, persisted queries, field-level authorization.
- Document the trade-offs honestly (ADRs), including where GraphQL is *not* the right call — this is as much a signal to interviewers as the working code.

## 3. Non-goals

- Not replacing the write side or re-implementing command handling — that's the existing ledger demo's job.
- Not a CRUD showcase. If the domain were flat/tabular, the pitch wouldn't hold, so this stays deliberately nested.
- Not attempting full GraphQL federation/gateway — that's a separate demo idea (multi-service federation) with its own justification.

## 4. Scope

**In scope**
- HotChocolate GraphQL server hosted on a .NET 10 minimal API host.
- Read model built from projections (either consuming events from the ledger demo's event store, or a seeded/synthetic projection store if built standalone).
- DataLoader-based batching for nested resolvers (transactions per account, entries per transaction, counterparty lookups).
- Query complexity/depth analysis and persisted-query allowlisting.
- A benchmark comparing GraphQL (1 query) against the equivalent REST call sequence (N calls) for the same nested view — round trips, payload size, latency.

**Out of scope (stretch, if time allows)**
- GraphQL subscriptions for live balance updates (ties in well with the telematics/real-time theme but isn't core to the CQRS argument).
- Angular consumer UI (reuse the ledger demo's frontend if convenient, otherwise Banana Cake Pop / GraphQL IDE is enough for the demo).

## 5. Phases

| Phase | Work | Exit criteria |
|---|---|---|
| 0 — Scaffolding | .NET 10 solution, HotChocolate wired into minimal API host, Docker Compose (Postgres + API) | `dotnet run` serves a GraphQL playground with a stub schema |
| 1 — Read model | Projection tables/documents for Account, Transaction, Entry; seed or event-consumer to populate them | Projections queryable directly against the store |
| 2 — Schema & resolvers | SDL-first schema matching the nested domain; root + nested resolvers | A hand-written nested query returns correct data (unoptimized) |
| 3 — Batching | DataLoader for transactions-by-account, entries-by-transaction, account-by-id (counterparty) | SQL/query logging shows flat batch counts instead of N+1 |
| 4 — Hardening | Query cost analysis, max depth, persisted query allowlist, field-level auth | An intentionally abusive deep/wide query is rejected with a clear error |
| 5 — Comparison & docs | Benchmark script (GraphQL vs REST round trips), ADRs, README, architecture diagram | Benchmark numbers and diagrams committed; README tells the story end to end |

Loosely: phases 0–2 in one sitting, 3–4 in a second, 5 as a wrap-up pass — but this is self-paced, not date-committed.

## 6. Tech stack

- .NET 10, C#
- HotChocolate (GraphQL server, DataLoader support built in)
- PostgreSQL for the projection store (EF Core or Marten, depending on whether projections are event-sourced or simple read tables)
- Docker Compose for local run
- Testcontainers for integration tests against the real projection store
- Optional: k6 or a small custom harness for the GraphQL-vs-REST benchmark

## 7. Success criteria

- A single GraphQL query fetches an account with its nested transactions, each transaction's entries, and each entry's counterparty account — in one HTTP round trip.
- The equivalent view assembled via REST requires visibly more round trips (documented in the benchmark), and the difference is quantified, not just asserted.
- DataLoader batching is demonstrated with a before/after query count (naive resolvers vs DataLoader) — this is the single most convincing artifact in the repo.
- ADRs exist for: "why GraphQL for the read side," "why not federation/gateway here," and "how query cost is bounded in production."
- README leads with the nested-query example and the comparison table, since that's the point of the whole demo.

