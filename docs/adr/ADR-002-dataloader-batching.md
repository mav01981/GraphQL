# ADR-002 — DataLoader batching over N+1-prone naive resolvers

**Status:** Accepted
**Date:** 2026-09
**Related:** [architecture.md](../architecture.md) §5, [docs/benchmark-results.md](../benchmark-results.md)

## Context

Naive GraphQL resolvers for `Account.transactions`, `Transaction.entries` and
`Entry.counterpartyAccount` execute once per parent. Resolving a statement for one account with 20
transactions × ~2 entries issues roughly 1 + 1 + 20 + 40 individual lookups — the classic N+1,
rebuilt one layer above the database.

## Decision

All sibling-facing nested resolvers resolve through GreenDonut DataLoaders
(`AccountByIdDataLoader`, `EntriesByTransactionDataLoader`). Requests issued within the same
execution tick are coalesced into a single batched EF query per entity type. The switch lives in
`ReadModel:UseDataLoaders` so the naive path remains available for measurement.

## Consequences

**Positive**

- Query count per statement is flat: measured at **4 SQL statements** for the full nested statement
  (account, its transactions, batched entries, batched counterparties) regardless of how many
  transactions/entries are nested — verified in `DataLoaderBatchingTests` and via the
  `x-sql-queries` response header.
- The win is demonstrated, not asserted: the naive path (`UseDataLoaders=false`) exists so the
  before/after number can be reproduced.

**Negative**

- DataLoader semantics (per-request scope, batching ticks, implicit dedup) are a genuine learning
  curve; a resolver that accidentally bypasses the loader reverts to N+1 silently.
- Batching implies `WHERE ... IN (...)` queries whose size scales with fan-out; page sizes are
  therefore capped by pagination limits.

## Alternatives considered

- **EF Core `Include()` from the root**: works for one fixed shape, but cannot serve client-chosen
  projections without loading everything; GraphQL's field selection would be cosmetic.
- **DTO composition in one stored query per client view**: collapses back into REST-shaped
  endpoints with fixed shapes — the exact thing this demo argues against on the read side.
