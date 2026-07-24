# ADR 0001: Contract-test the client against the committed OpenAPI document

- Status: Accepted
- Date: 2026-07-24
- Issue: #52

## Context

The Management API client is split across two styles:

- **Kiota-generated request builders** (`src/Umbraco.Cli.Client/Generated`) for the
  read paths and a few others. These are generated *from* `spec/management.json`, so
  any endpoint they call provably exists in the spec at generation time; if a future
  regen removes an endpoint the CLI uses, the generated builder disappears and the
  adapter **fails to compile**. This path is therefore already guarded by the compiler.
- **Hand-written `HttpClient` calls** in `UmbracoManagementClient` for the write paths
  (create/update/delete/publish), dictionary and a few reads. These embed URL templates
  as string literals. Nothing checks that those strings still match a real endpoint, so
  they are the genuine drift risk (and were the root cause of the #39/#40/#44 bugs).

We want a cheap CI check that fails when an endpoint the CLI depends on is absent from
the target instance's OpenAPI document, without needing a live Umbraco (that is #51).

## Decision

Add a unit test (`ContractTests`) that:

1. Loads the committed `spec/management.json` (located by walking up from the test
   assembly's base directory to the repo root).
2. Holds a **curated contract**: the explicit set of `(HTTP method, path template)`
   pairs the CLI depends on — every hand-written endpoint (the drift-risk set) plus the
   key Kiota read endpoints for documentation.
3. Asserts each pair exists in the spec's `paths`/verbs. The test fails if any is absent.

The curated list is intentionally explicit rather than reflected out of the client:
the hand-written URLs are string literals with no runtime registry to enumerate, and an
explicit list doubles as human-readable documentation of the CLI's API surface. When a
hand-written endpoint is added or changed, the list is updated in the same change; the
test then guards that the endpoint exists in the (possibly regenerated) spec.

## Consequences

- Catches spec drift for the hand-written endpoints on every `dotnet test` run — no live
  instance, milliseconds to run.
- The curated list must be kept in sync by hand for hand-written endpoints. This is
  acceptable: those endpoints change rarely and always in a deliberate edit to the client.
- Does not verify request/response *shapes* — only endpoint existence. Shape correctness
  is covered by the live integration harness (#51).
