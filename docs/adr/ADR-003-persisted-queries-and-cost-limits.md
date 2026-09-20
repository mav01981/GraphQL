# ADR-003 — Persisted queries + cost limiting for production posture

**Status:** Accepted
**Date:** 2026-09
**Related:** [architecture.md](../architecture.md) §6, [spec.md](../spec.md) §7

## Context

A client-driven query language is an attack surface: without bounds, a single request can ask the
server to traverse the `entry → counterpartyAccount → transactions → entries...` cycle indefinitely,
or fan out wide enough to exhaust the read store. Meanwhile the caching story is weaker than REST's
HTTP-cache-by-URL, because GraphQL requests are POSTs with arbitrary bodies.

## Decision

Three defenses, layered:

1. **Depth limiting** (`GraphQL:MaxExecutionDepth = 8`) — the counterparty cycle cannot be walked
   past the documented statement shape. Violations are rejected before execution with
   `MAX_DEPTH_EXCEEDED`.
2. **Cost analysis** (`GraphQL:MaxFieldCost = 1000`, `MaxTypeCost = 500`) — field/list weights bound
   the total work a query can request; violations fail fast with `COST_LIMIT_EXCEEDED`. Cost is also
   logged per request (`CostLoggingDiagnosticEventListener`) so drift in client queries is visible.
3. **Persisted operation allowlisting** — clients send the registered documents from `operations/`
   (published at startup by `PersistedOperationAllowlist`). Arbitrary ad-hoc query text can be
   disabled (`OnlyAllowPersistedOperations`), and known queries can be cached by hash.

## Consequences

**Positive**

- The read model's worst-case request is bounded by configuration, not client discipline.
- Persisted queries restore a cacheable, reviewable contract: a query change is a code-reviewed
  artifact, like an endpoint change in REST.
- The abusive-query rejection is tested (`QueryLimitsTests`) rather than aspirational.

**Negative**

- Ad-hoc exploration (Nitro, formerly Banana Cake Pop — see ADR-005) fights the allowlist unless `AllowDocumentBody` is left on in
  non-production; that toggle must be off in real deployments.
- Every new client view requires registering a persisted document — a small but real process cost.

## Alternatives considered

- **Trusting clients / rate limiting only**: rejected — rate limits bound frequency, not per-request
  cost; one deep query can still be expensive.
- **Complexity analysis only, no persisted queries**: viable, but leaves no stable contract to cache
  or review; the combination is what makes the read side shippable.
