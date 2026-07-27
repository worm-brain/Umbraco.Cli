# ADR 0003: Migrate the write path to the generated Kiota client

- Status: Accepted
- Date: 2026-07-27
- Issue: #50 (epic), folds in #74, splits out #79 (content writes + media upload)
- Supersedes the "transitional hybrid" state described in ADR/notes for the alpha.2 generation work.

## Context

Issue #50 replaced the hand-written Management API client with a Kiota client
generated from the committed OpenAPI spec (`spec/management.json`, Umbraco
17.3.5). That work shipped in alpha.2 but only migrated the **read** path. The
client is currently a **transitional hybrid**: `UmbracoManagementClient` holds
both a raw `HttpClient` (`_http`) and a Kiota `UmbracoApiClient` (`_api`).

Still on the hand-written `_http` path:

- **Writes:** content create/update/delete/publish/unpublish, media
  upload/delete, document-type create/delete, language create/delete, member
  create/delete, user invite, dictionary create, webhook create/delete.
- **Reads (overlooked in alpha.2):** document-type get-by-id, data-type
  get-by-id, users list + get-by-id, dictionary list + get-by-key, webhooks list.

The hand-written path carries behaviour the generated path must preserve:

- **#43** — Umbraco writes return `201 Created` with an *empty* body and a
  `Location` header; `DeserializeAsync` treats 2xx-empty as success and hydrates
  the new id from `Location`.
- **#48** — `BuildErrorAsync` + `FormatValidationErrors` flatten RFC-9110
  ProblemDetails, including the field-level `errors` map ("which field failed").
- **#46/#47** — request DTOs carry every API-required field; webhook events are
  objects.

## Decision

The intended end-state is: migrate **all** remaining hand-written calls to the
generated client and delete `_http` + the hand-written helper stack entirely.

### Scope split (revised 2026-07-27)

Concretely scoping the migration surfaced that two resources drag in real
correctness work beyond a mechanical port, so #50 is split:

- **This ticket (#50):** migrate the mechanically-clean surface —
  - **Deletes** → Kiota: content, media, document-type, language, member, webhook.
  - **Creates** → Kiota (pre-gen GUID + GET-hydrate, fixes #74 for these):
    language, member, dictionary, webhook.
  - **User invite** → Kiota (void POST, no hydrate).
  - **Reads** (overlooked in alpha.2) → Kiota: document-type by-id, data-type
    by-id, user list + by-id, dictionary list + by-key, webhook list.
  - **Error parity:** extend `GuardedApiAsync` to flatten ProblemDetails
    field-level errors (#48).
  - Delete the now-unused `GetAsync` and `DeleteAsync` helpers.

- **Follow-up issue:** the content **write** path (create/update/publish/
  unpublish) and **media upload**. These need document-type **alias -> id**
  resolution, a **JSON -> `UntypedNode`** converter for property-editor values,
  and the two-step **temporary-file** upload flow — and the current
  content-create-by-alias path looks broken (sends `documentType.id` =
  `Guid.Empty`) and is untested. `_http` and the `PostAsync`/`PutAsync`/
  `SendAsync`/`DeserializeAsync`/`BuildErrorAsync` helpers stay until then, used
  only by those deferred calls.

### Key design points

1. **Created-id via client-supplied GUID (replaces #43's Location parsing).**
   The generated create methods are `PostAsync` returning `Task` (void) — no
   body, and Kiota cannot expose the `Location` header without a
   `ResponseHandlerOption` that also bypasses its error-mapping. But every
   generated create model carries a settable `Guid? Id`, and Umbraco 14+ accepts
   a client-generated GUID. So we **pre-generate the id, set `body.Id`, POST,
   and already know the new id** — no Location header, no raw-response handling,
   and error-mapping stays intact. (Bonus: deterministic ids, relevant to #63.)

2. **Fix #74 (empty create payload) via POST-then-GET.** Because the create
   response body is empty, the current create commands echo only the id, leaving
   name/url/alias blank (#74). After a successful create we **GET the new
   resource by its id** and return the fully-hydrated object. If the follow-up
   GET fails, we still return success with the id (the create *did* succeed) —
   the GET is best-effort hydration, never a reason to fail a completed write.
   Update follows the same re-GET pattern.

3. **Preserve #48 field-level errors.** `GuardedApiAsync`'s `Gen.ProblemDetails`
   handler currently builds a message from `detail`/`title`/status only — it
   drops the field-level `errors` map. Before migrating writes, extend it to
   flatten `ProblemDetails.Errors`/`AdditionalData["errors"]` so a rejected
   create still tells the user which field failed. This is the one behaviour
   most at risk in the migration.

4. **Media upload via the temporary-file flow (pulls #57 core forward).** The
   generated media create is plain JSON (`CreateMediaRequestModel`, no file
   part). Umbraco 14+ uploads a file in two steps: POST the bytes to the
   `temporary-file` endpoint, then create the media referencing that temp-file
   id inside a `MediaValueModel` in `Values`. Rather than keep `_http` alive for
   the one multipart call, we implement this two-step flow now (decision on this
   ticket), so `_http` is deleted entirely in #50. #57 then shrinks to exposing
   standalone `temporary-file` commands over the same client method.

### Approach: TDD, one resource slice at a time

Per the grill, each slice is worked test-first and committed independently:

- Keep the existing unit suite (FakeUmbracoManagementClient contract) green.
- Add a **live create -> get -> delete round-trip** to the #51 integration
  harness for each write resource (self-cleaning), asserting the created id
  **and** name are echoed (guards #74). These run against the local 17.3.5
  instance and skip where none is reachable.
- Slice order: error-mapping parity -> content -> media -> document-type ->
  data-type -> language -> member -> user -> dictionary -> webhook -> remove
  `_http` + helpers.

`user invite` sends a real email, so it is migrated and unit-tested but **not**
exercised by a live round-trip.

## Consequences

- The client is fully generated; request/response drift on writes is eliminated
  at its source (the same win reads already got). ~400 lines of hand-written
  HTTP plumbing are deleted.
- Every create costs one extra GET round-trip (the #74 hydration). Acceptable
  for a CLI/agent tool where a correct, complete payload matters more than one
  call; the create still succeeds if the GET fails.
- Cross-version (15/16) verification remains out of scope — we only have a 17.3.5
  instance; tracked separately (same infra gap as #77).
- Once merged, #50 closes and #74 is verified fixed by the content/media/etc
  round-trips.
```
