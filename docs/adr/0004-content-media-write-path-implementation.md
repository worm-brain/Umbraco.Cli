# ADR 0004: Content/media write-path migration - implementation decisions

- Status: Accepted
- Date: 2026-07-28
- Issue: #79 (follow-up to #50; see ADR 0003)
- Builds on: ADR 0003 (which decided the *strategy* - client-supplied GUID,
  POST-then-GET hydration, two-step temporary-file upload, slice order - and
  deferred the concrete *how* of the correctness-heavy pieces to this ticket).

## Context

ADR 0003 forecast three pieces of #79 as needing real design beyond a mechanical
port: document-type/member-type **alias -> id** resolution, a **JSON ->
`UntypedNode`** converter for property-editor values, and the media **dry-run**
interaction with the two-step temporary-file flow. This ADR records the
decisions taken for each during the #79 grill.

## Decisions

### 1. Type-reference resolution: preserve the alias contract via a tree walk

`content create --content-type` and `member create --type` are documented to
take a **type alias** (e.g. `textPage`). The generated `CreateDocumentRequestModel`
/ `CreateMemberRequestModel` only accept a type **id** (`ReferenceByIdModel`), so
the alias must be resolved to an id first.

The existing template resolver (`GetTemplateByAliasAsync`) cannot be copied
verbatim: the template *search item* model exposes `Alias`, but
`DocumentTypeItemResponseModel` (and the member-type equivalent) expose only
`Name`/`Id` - **no `Alias`**. So an alias cannot be matched against the search
result directly.

**Decision:** keep the alias contract (no breaking change to the CLI). Resolve
by: if the input parses as a GUID, use it directly; otherwise walk the
document-type **tree** (`tree/document-type/root`, recursing into folders via
`tree/document-type/children`) and GET each non-folder candidate's full
`DocumentTypeResponseModel` (which *does* carry `Alias`) to exact-match on alias,
case-insensitively. On no match, return a clean `NotFound` naming the `list`
command. The same shape resolves member-type via its tree + by-id endpoints.

The tree walk and every alias seen are cached per client instance, and the scan
short-circuits on the first match, so the usual one-alias-per-invocation case
stops as soon as it is found.

- **Superseded first attempt (the search):** the original decision here was
  search-then-GET (`item/document-type/search?query=<alias>`). The open risk
  below materialised: **the item search indexes only `Name`, not `Alias`.** A
  query by alias therefore returns nothing whenever the alias differs from the
  name by more than case - which is the Umbraco norm for any multi-word type
  ("Text Page" -> `textPage`). Verified live against 17.3.5: `base` and
  `vendorHubHome` resolved only because their *names* happen to match the alias
  string, while `vendorHubContact` (name "Vendor Hub Contact") returned a bogus
  404. That is the majority of real types, so the search path was abandoned for
  alternative (b) below. `ContentWriteClientTests.
  CreateContentAsync_AliasDiffersFromName_ResolvesWithoutItemSearch` asserts the
  search is not used, and the live stdin/dry-run test asserts the previewed
  `documentType.id` equals the id the alias should resolve to.
- **Alternative rejected:** switch the CLI to match on *Name* - simpler (one
  search, no extra GET) and mirrors the media-type resolver, but a breaking
  change to a documented contract; developers know aliases, not display names.
- **Cost:** the tree walk plus one GET per candidate until the alias matches.
  Chattier than a search would have been, but it is the only correct option
  given the item model carries no alias, and a CLI/agent tool needs the
  reference right more than it needs the round-trip. Note the tree root alone is
  *not* enough - types nested in folders would be invisible, which is also why
  `content-types list` under-reports (issue #97).

This also fixes the currently **broken, untested** create-by-alias path (ADR
0003 noted it serializes `documentType.id = Guid.Empty`).

### 2. JSON -> `UntypedNode` converter: typed number mapping

Property-editor values arrive as `JsonElement` (from `--json-body`) and must
become the `UntypedNode? Value` on `DocumentValueModel`/`MediaValueModel`/
`MemberValueModel`. Kiota exposes no public `JsonElement -> UntypedNode` factory,
so this is net-new, recursive code. Objects, arrays, strings, booleans and null
map losslessly to `UntypedObject`/`UntypedArray`/`UntypedString`/
`UntypedBoolean`/`UntypedNull`. Numbers are the only ambiguous case.

**Decision:** map numbers by narrowest faithful .NET type - integer fitting
`Int32` -> `UntypedInteger`; larger integer -> `UntypedLong`; non-integer ->
`UntypedDecimal` (`decimal` avoids the rounding that `double` would introduce for
money-like values); `UntypedDouble` only as a last resort. This is lossless for
every common Umbraco payload and is directly unit-testable (an AC of #79).

- **Alternatives rejected:** a custom `UntypedNode` writing `GetRawText()` for
  exact wire fidelity (overkill; a hand-rolled serializer to maintain), and
  mapping everything to `double` (loses precision for decimals and large ints).

### 3. Media `--dry-run`: skip staging, preview the media create

The upload is two mutations - POST bytes to `/temporary-file`, then POST the
media referencing the temp-file id. The transport-level mutation interceptor
throws on the *first* mutation, so `--dry-run` previewed only the temp-file
upload and aborted, never showing the media create the user cares about (noted on
#62). The temp-file id is client-generated up front, so the media-create body can
be built without staging any bytes.

**Decision:** in Preview (dry-run) mode, `UploadMediaAsync` reads the
process-scoped mutation policy, **skips the temp-file upload** (so nothing is
staged - dry-run still mutates nothing), and lets the media-create POST reach the
interceptor, which previews the meaningful operation with the pre-generated
temp-file id.

- This introduces one small, localized coupling: the client reads the dry-run
  policy in this single genuinely two-step method. Accepted over the alternative
  of reworking the whole single-exception dry-run model into a multi-step
  accumulator (touches the interceptor, executor and output envelope, and changes
  behaviour for every command).

## Consequences

- The CLI's alias-based `--content-type`/`--type`/`--media-type` inputs keep
  working; the previously broken content-create-by-alias path is fixed and gains
  live round-trip coverage.
- `_http` and its nine helpers are deleted once all #79 slices land (single PR,
  commit per slice in ADR 0003's order); the client becomes fully generated **except** for the
  raw-JSON passthrough of ADR 0005 §1 - schema, content export/apply, and (per the 2026-09-24
  amendment below) `content update`.
- Read migrations (doc-type/data-type by-id, user list + by-id, dictionary list +
  by-key, webhook list) must preserve their current JSON output contract - the
  integration assertions guard this (see the testing memory note).

---

## Amendment, 2026-09-24: `content update` leaves the typed write path (#178/#179)

- Status: Accepted, amends the **Consequences** below (and ADR 0003's "the client is fully
  generated") for **update only**
- Issue: #178, #179 (found in the 2026-09-23 hands-on round, tracked under #187)

### What changed

`UpdateContentAsync` no longer builds a `Gen.UpdateDocumentRequestModel`. It reads the document
verbatim with `GetRawJsonAsync`, overlays the request onto it, and PUTs the result - the
raw-JSON passthrough of ADR 0005 §1, already used by `UpdateRawScalarsAsync` and the content
export/apply pipeline (ADR 0006).

### Why

`PUT /document/{id}` is replace-semantics, not patch. A typed request body can only carry the
fields the CLI models, so everything else was deleted on every update:

- the document's **template**, which `UpdateContentRequest` did not carry at the time. Republishing then
  404'd the page ("No physical template file was found..."). On a live 17.7.0 test site this hit
  all 22 nodes.
- every **property value the caller did not restate**, because the API treats an absent value as
  a cleared one.

Neither was reachable through the typed model without growing it to mirror the whole document -
at which point it is the raw body with extra steps. This is the same conclusion ADR 0005 §1
reached for schema, arrived at again from the other direction.

### Scope and consequences

- **Create is unchanged.** It still uses the typed `CreateDocumentBody`, because a create has no
  prior state to preserve and Umbraco 17 requires the `template` key to be present (#134).
- The request models gain a template (#162): `ContentTemplateReference` (id **or** alias) on both
  `CreateContentRequest` and `UpdateContentRequest`, surfaced as `--template <alias|uuid>` on
  `content create` and `content update`. On update, omitting it means "keep the current template",
  never "remove it"; the flag overrides whatever the body carries.
- `DocumentUpdateBody` holds the merge rules as a pure, synchronous type: values keyed on
  (alias, culture, segment), variants on (culture, segment), every entry projected to the
  request's own fields so read-only extras (`editorAlias`, `state`) are not echoed back.
- **Merge is the default; `--replace` opts into the old wholesale behaviour** for values and
  variants. The template survives either way - dropping it was never intended behaviour.
- A variant that names no culture, sent against a document that varies by culture, is **rejected**
  rather than appended: it identifies no variant, and Umbraco rejects the resulting mix anyway.
- The read-then-write introduces a lost-update window with no concurrency control. Tracked as
  #188, pending an answer on whether Umbraco 17 emits `ETag` / honours `If-Match`.
