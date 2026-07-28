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

### 1. Type-reference resolution: preserve the alias contract via search-then-GET

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
by: if the input parses as a GUID, use it directly; otherwise search
(`item/document-type/search?query=<alias>`), then GET each candidate's full
`DocumentTypeResponseModel` (which *does* carry `Alias`) and exact-match on
alias. On no match, return a clean `NotFound`. The same shape resolves
member-type via its search + by-id endpoints.

- **Alternatives rejected:** (a) switch the CLI to match on *Name* - simpler
  (one search, no extra GET) and mirrors the media-type resolver, but a breaking
  change to a documented contract; developers know aliases, not display names.
  (b) page the entire type list and match alias - avoids depending on search
  query semantics but is chattier for large sites.
- **Open risk to verify live in TDD:** whether the search `query` parameter
  actually indexes alias. If a query-by-alias returns no candidate, fall back to
  paging the full type list and matching alias. The live content-create
  round-trip against 17.3.5 is the gate.
- **Cost:** one extra GET per candidate at create time. Acceptable for a
  CLI/agent tool where a correct reference matters more than a round-trip.

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
  commit per slice in ADR 0003's order); the client becomes fully generated.
- Read migrations (doc-type/data-type by-id, user list + by-id, dictionary list +
  by-key, webhook list) must preserve their current JSON output contract - the
  integration assertions guard this (see the testing memory note).
