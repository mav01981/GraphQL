# ADR-005 — Nitro (Banana Cake Pop successor) as the GraphQL API documentation surface

**Status:** Accepted
**Date:** 2026-09-20
**Related:** [architecture.md](../architecture.md) §6, [spec.md](../spec.md) §4/§10, ADR-003, `src/Ledger.GraphQL.Api/Program.cs`, `src/Ledger.GraphQL.Api/GraphQLServiceCollectionExtensions.cs`

## Context

The read-side API is a single HotChocolate GraphQL endpoint (`POST /graphql`) with a
nested, client-driven schema (`Account → Transaction → Entry → CounterpartyAccount`).
REST-style documentation (one page per URL, e.g. Swagger UI over OpenAPI) does not
describe that surface: every client view is a different selection set over the same
endpoint, plus field-level `@authorize` policies, cost/depth limits and persisted
operations change what a caller can actually execute.

We need an API-documentation story that:

1. Shows the live schema (types, fields, enums, `@authorize`/`@cost` directives) generated
   from the code, not a hand-maintained copy.
2. Lets a reviewer execute the headline `AccountStatement` query with each demo role
   (`viewer` / `accountant` / `auditor`) and see field-level degradation in place.
3. Adds no extra service, package, or deployment step to the demo.
4. Does not weaken the production posture from ADR-003 (depth/cost limits, persisted-
   operation allowlist, no anonymous schema probing in production).

## Decision

Use the GraphQL IDE bundled with `HotChocolate.AspNetCore` 16 — **Nitro**, the renamed
successor of **Banana Cake Pop** — as the API documentation and explorer. No additional
package (no `ChilliCream.Nitro.App` reference, no separate docs site) is required:
`HotChocolate.AspNetCore` already serves it.

Concrete wiring:

- `POST /graphql` stays the API and stays `RequireAuthorization()` — the token is
  validated once per request, field policies then gate `balance` / `amount` /
  `counterpartyAccount`.
- In `Development` only, serve an anonymous explorer on a separate path so the browser
  itself is not blocked by the endpoint authorization:

  ```csharp
  if (app.Environment.IsDevelopment())
  {
      app.MapDevTokenEndpoints();
      app.MapNitroApp("/graphql/ui").AllowAnonymous();
  }
  ```

  Production therefore exposes no IDE; the API endpoint keeps requiring a JWT.
- Keep introspection enabled in Development (HotChocolate default; do not call
  `AllowIntrospection(false)` there). The `Schema Reference` pane, completion, and the
  static SDL file (`GET /graphql/schema.graphql`) all derive from it. If introspection
  is ever disabled globally, Nitro degrades to “`Introspection is not allowed HC0046`”.
- Keep `GraphQL:PersistedOperations:AllowDocumentBody=true` in Development so ad-hoc
  Nitro queries execute; `OnlyAllowPersistedOperations` remains available for
  production/CI (see ADR-003).
- Schema documentation comes from code: `descriptor.Description(...)` /
  `[GraphQLDescription(...)]` on `Query`, `ObjectTypes`, and the `*Resolvers`, plus
  enum value names and the built-in `@authorize`/`@cost` directive metadata. The
  allowlisted documents in `src/Ledger.GraphQL.Api/operations/` (`account-statement`,
  `account-balances`) double as copy-paste examples inside the IDE.

Usage is documented in the solution `README.md` (“Exploring the API with Nitro”):
run the API, open `http://localhost:5080/graphql/ui`, mint a role token from
`GET /dev/token/{viewer|accountant|auditor}`, add it as an `Authorization: Bearer …`
header in Nitro’s connection settings, and run the statement query.

## Consequences

**Positive**

- Zero-dependency docs: the explorer and the schema cannot drift from the code because
  they are rendered from the running schema.
- Reviewer flow is one URL: open the IDE, paste a token, run the same query as three
  roles, watch `AUTH_NOT_AUTHORIZED` null only the gated fields. That is the demo’s
  core talking point made interactive.
- No production surface added: the IDE route does not exist outside Development, and
  `/graphql` itself still requires authentication plus depth/cost enforcement.

**Negative**

- Nitro needs the request JWT configured manually (HTTP header in connection settings);
  there is no built-in “log in as viewer” button — mitigated by `/dev/token/{role}` and
  the README steps.
- `Latest` serve mode loads the IDE from a CDN; offline/air-gapped reviewers must switch
  to `ServeMode.Embedded` via `WithOptions(o => o.ServeMode = ServeMode.Embedded)`.
- Introspection + ad-hoc bodies stay on in Development by design; a misconfigured
  production deployment that copies Development settings would expose the full schema.
  This is accepted because `ASPNETCORE_ENVIRONMENT` gates the route, matching how
  `/dev/token` is already gated.

## Alternatives considered

- **Swagger/OpenAPI + Swashbuckle/NSwag:** rejected — describes REST resources, not a
  single-endpoint GraphQL schema with selection sets, connections, and per-field
  policies. Would be a second, lossy contract to maintain.
- **External GraphiQL / GraphQL Playground / Postman:** rejected — works, but requires
  the reviewer to install/configure something and point it at the endpoint with auth
  headers. Nitro is already in the dependency graph and pre-wired to `/graphql`.
- **Standalone static docs site (e.g. checked-in SDL + hand-written pages):** rejected
  as the primary surface — the SDL file (`/graphql/schema.graphql`) remains useful as
  an artifact, but alone it is not executable and rots without a snapshot test.
- **Custom `/docs` endpoint or second `AddGraphQLServer().AllowIntrospection()` chain:**
  rejected — a second server registration creates a second, empty schema and breaks
  authorization/cost wiring. Introspection and tooling are configured on the single
  `AddLedgerGraphQL()` builder instead.
