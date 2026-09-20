# Technical Specification — CQRS Read-Side via GraphQL (.NET 10)

Companion to `plan.md` and `architecture.md`. Those set the "why"; this sets the "exactly what" — schema, data flow, and non-functional targets, precise enough to build against.

## 1. Purpose & scope

Defines the read-side service: a standalone HotChocolate GraphQL API on .NET 10, backed by a seeded projection store, with full field-level authorization. No dependency on the ledger CQRS demo's live event store — this spec treats the read side as buildable and demoable on its own.

## 2. Data source & seeding strategy

**Decision:** standalone, seeded/synthetic data. No event consumer, no coupling to the ledger demo's write side or event store for this version.

- A deterministic seed generator (fixed RNG seed, so runs are reproducible) populates the projection store directly on startup or via a `--seed` CLI flag.
- Generated data must respect double-entry invariants: every transaction's entries sum to zero per currency, so the read side is exercising realistic, internally-consistent data rather than arbitrary fixtures.
- Suggested scale for demo/benchmark purposes: ~1,000 accounts, ~20,000 transactions, ~45,000 entries — large enough that the N+1-vs-DataLoader comparison in `architecture.md` produces a visible difference, small enough to seed in seconds.
- **Extension point (out of scope for this version, but worth stubbing):** an `IProjectionSource` interface behind the seeder, so a future iteration could swap in a real event consumer from the ledger demo without touching the GraphQL layer. Spec it as an interface only — don't implement the event-consuming side here.

## 3. Read model schema (projection store — Postgres)

```sql
accounts
  id            uuid PK
  name          text NOT NULL
  type          text NOT NULL   -- ASSET | LIABILITY | EQUITY | REVENUE | EXPENSE
  currency      char(3) NOT NULL
  balance       numeric(18,2) NOT NULL   -- denormalized, maintained at seed time
  created_at    timestamptz NOT NULL

transactions
  id            uuid PK
  posted_at     timestamptz NOT NULL
  description   text
  reference     text

entries
  id              uuid PK
  transaction_id  uuid FK -> transactions.id
  account_id      uuid FK -> accounts.id
  amount          numeric(18,2) NOT NULL
  direction       text NOT NULL   -- DEBIT | CREDIT

-- indices
entries(account_id)
entries(transaction_id)
transactions(posted_at)
```

## 4. GraphQL schema (SDL)

Cursor-based (Relay-style) pagination throughout, since this is meant to read as production-realistic rather than demo-shortcut pagination.

```graphql
type Query {
  account(id: ID!): Account
  accounts(first: Int, after: String, filter: AccountFilterInput): AccountConnection!
  transaction(id: ID!): Transaction
}

type Account {
  id: ID!
  name: String!
  type: AccountType!
  currency: String!
  balance: Money! @authorize(policy: "ViewBalance")
  transactions(first: Int, after: String): TransactionConnection!
}

type Transaction {
  id: ID!
  postedAt: DateTime!
  description: String
  reference: String
  entries: [Entry!]!
}

type Entry {
  id: ID!
  amount: Money! @authorize(policy: "ViewAmount")
  direction: Direction!
  counterpartyAccount: Account @authorize(policy: "ViewCounterparty")
}

type Money {
  amount: Decimal!
  currency: String!
}

enum Direction { DEBIT CREDIT }
enum AccountType { ASSET LIABILITY EQUITY REVENUE EXPENSE }

type AccountConnection { edges: [AccountEdge!]! pageInfo: PageInfo! }
type AccountEdge { cursor: String! node: Account! }
type TransactionConnection { edges: [TransactionEdge!]! pageInfo: PageInfo! }
type TransactionEdge { cursor: String! node: Transaction! }
type PageInfo { hasNextPage: Boolean! endCursor: String }

input AccountFilterInput {
  type: AccountType
  nameContains: String
}
```

## 5. Authorization model (full field-level)

**Decision:** production-realistic, field-level, not just request-level.

Roles/claims used by the demo (issued as JWT claims by a local test token issuer — Duende's in-memory test mode or a hand-rolled dev STS is enough, no real IdP needed):

| Role | Can see |
|---|---|
| `viewer` | Account name, type, currency, transaction metadata (no amounts, no counterparties) |
| `accountant` | Everything `viewer` sees, plus `balance` and `entry.amount` |
| `auditor` | Everything `accountant` sees, plus `counterpartyAccount` (cross-account relationship visibility) |

Enforcement:
- HotChocolate `@authorize(policy: "...")` directives on the three sensitive fields (`Account.balance`, `Entry.amount`, `Entry.counterpartyAccount`), backed by ASP.NET Core authorization policies registered in DI.
- Evaluated **per field**, not per request — a `viewer` token querying the full `AccountStatement` shape from `architecture.md` still gets a `200` with data, just with `balance`, `amount`, and `counterpartyAccount` resolving to `null` and a corresponding entry in the GraphQL `errors` array (extension code `AUTH_NOT_AUTHORIZED`). This partial-result behavior is a deliberate GraphQL-vs-REST talking point worth calling out in the demo README: a REST endpoint typically has to be all-or-nothing (200 or 403) per resource, where field-level auth degrades gracefully per field.
- Seed at least three demo JWTs (one per role) so a reviewer can try the same query three times and see the field-level difference directly.

## 6. Data flow

1. Startup: seeder populates Postgres directly (no event store involved) — idempotent, safe to re-run.
2. Client sends a GraphQL POST with a bearer token.
3. HotChocolate pipeline: parse → validate → **request-level** auth (is the token valid at all) → cost analysis → execute.
4. Root resolvers (`account`, `accounts`, `transaction`) query Postgres directly via EF Core.
5. Nested resolvers (`transactions`, `entries`, `counterpartyAccount`) resolve through DataLoaders, batched within the same execution tick — see `architecture.md` §5 for the batching mechanics.
6. Field-level `@authorize` policies evaluated per field during resolution; unauthorized fields null out with an error, the rest of the response completes normally.
7. Single JSON response returned to the client — one round trip regardless of role.

## 7. Non-functional requirements

Flagged throughout as **indicative demo-scale targets**, not load-tested production SLAs — useful for showing you think in these terms, not a claim the repo includes a real perf test rig.

| Aspect | Indicative target | Notes |
|---|---|---|
| Latency (nested AccountStatement query, seeded ~1k accounts / 20k tx) | p50 < 50ms, p95 < 200ms | Single instance, local Postgres, no network hop |
| Throughput | ~100 req/s sustained, single instance | Rough ceiling before adding read replicas/caching would matter |
| Query cost budget | 1,000 cost points per request | HotChocolate cost directive; tune per-field weights so the AccountStatement query sits comfortably under budget, abusive deep queries don't |
| Max query depth | 8 | Blocks the `entry → counterpartyAccount → transactions → entries → ...` cycle from being exploited |
| Caching | Persisted-query hash cache; optional 30s result cache for read-heavy paths | No HTTP-cache-by-URL (GraphQL is POST-based), so caching strategy has to be explicit rather than free |
| Observability | OpenTelemetry trace per resolver; query cost logged per request | Cost logging in particular is useful evidence for the "we bound this" argument in interviews |
| Scalability | Stateless API layer, horizontally scalable | DataLoader is scoped per-request, so no shared state blocks scale-out |
| Availability | Not a target | Single instance is fine for a portfolio demo — worth stating explicitly rather than pretending otherwise |

## 8. Error handling

- Validation errors (malformed query, unknown field) rejected before execution, standard GraphQL error shape.
- Authorization failures null the specific field and add an error with extension code `AUTH_NOT_AUTHORIZED` — request otherwise succeeds.
- Cost-limit or depth-limit violations rejected **before execution starts** (fail fast, no wasted DB work) with extension codes `COST_LIMIT_EXCEEDED` / `MAX_DEPTH_EXCEEDED`.
- Not-found entities (`account(id: ...)` with an unknown ID) return `null` for that field rather than an error, per GraphQL convention.

## 9. Testing strategy

- Unit tests for authorization policies (one test per role × per protected field).
- Integration tests via Testcontainers against real Postgres with a fixed seeded fixture set (not the full-scale seed — a small deterministic dataset for assertions).
- DataLoader batching verified via query-count assertions: naive resolver vs DataLoader-backed resolver, asserting the actual SQL call count drops from O(n) to O(1) per entity type.
- Cost/depth limit tests: assert specific abusive queries (deeply nested, or wide `accounts(first: 10000)`) are rejected with the right extension code.
- Schema snapshot test (HotChocolate supports this) to catch accidental breaking schema changes.

## 10. Local dev / deployment

- `docker-compose.yml`: Postgres + API.
- `dotnet run --seed` (or a separate console project) to populate the projection store.
- `appsettings.Development.json` includes the three demo JWTs (viewer/accountant/auditor) so a reviewer can try all three without standing up a real STS.

## 11. Out of scope (this version)

- Real event consumption from the ledger demo's event store (stubbed as `IProjectionSource` only, per §2).
- Subscriptions / real-time updates.
- Federation or multi-service schema stitching.
- Load-tested, production-grade perf numbers (targets in §7 are indicative only).
