# ADR-004 — No federation/gateway in this demo

**Status:** Accepted
**Date:** 2026-09
**Related:** [plan.md](../plan.md) §3, [architecture.md](../architecture.md) §8

## Context

HotChocolate supports federation/gateway patterns (schema stitching across subgraphs). It is
tempting to include one "because it's in the framework", but this demo is a single bounded context:
accounts, transactions and entries are one read model backed by one store.

## Decision

Federation is explicitly out of scope. One API, one schema, one read model.

## Consequences

**Positive**

- The demo keeps making one argument well: where GraphQL earns its place on a nested read model.
  A gateway would dilute that into an infrastructure demo.
- No supergraph composition, entity resolver contracts or multi-service deployment story to
  maintain or explain.

**Negative**

- The repo does not demonstrate the federation skills some teams need at scale. That is a separate
  demo with its own justification (multiple genuinely independent bounded contexts, each owning its
  schema slice).

## When this decision would be revisited

- A second bounded context (e.g. customers, or an FX-rates service) needs to compose its fields
  into the ledger graph.
- Ownership of the read model splits across teams, making a single schema a coordination bottleneck.
