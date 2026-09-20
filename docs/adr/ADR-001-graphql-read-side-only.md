# ADR-001 — GraphQL for the read side only; the write side stays REST/gRPC + MediatR

**Status:** Accepted
**Date:** 2026-09
**Related:** [plan.md](../plan.md) §1, [architecture.md](../architecture.md) §4

## Context

The ledger system is CQRS end to end: commands flow through a MediatR pipeline behind REST/gRPC,
and read models are projected from the write side. The read model for an account statement is
deeply nested (`Account → Transaction → Entry → CounterpartyAccount`) and its shape varies by
consumer: a balance widget needs two scalars, an auditor's statement needs the whole graph.

## Decision

GraphQL is used **only** as the query-side surface over the projections. Nothing in the GraphQL
layer can write: the schema is read-only, the EF `ReadDbContext` is used with `AsNoTracking()`
queries, and the write side keeps its existing command endpoints untouched.

## Consequences

**Positive**

- The nested read model can be fetched in one round trip with exactly the fields the client wants.
- The write side keeps the discipline of explicit command contracts; GraphQL's flexibility never
  touches state mutation.
- The mutation-shaped problems of GraphQL (idempotency, relay-style mutation payloads, optimistic
  concurrency over mutation results) are simply not imported.

**Negative**

- Two API styles live in the codebase, so contributors must learn both.
- Cross-style documentation is needed so clients know which surface to use (reads → GraphQL,
  writes → REST commands).

## Alternatives considered

- **GraphQL for everything**, including mutations: rejected — it would couple the command pipeline's
  authorization, validation and auditing story to a resolver graph, gaining nothing the write side needs.
- **REST only**: rejected for reads — see `architecture.md` §4; the nested domain forces N round trips
  or severe over-fetching.
