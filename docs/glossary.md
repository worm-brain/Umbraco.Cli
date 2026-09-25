# Glossary

Project-specific terms. Add entries as concepts are introduced.

- **Contract test** — a fast unit test that asserts the set of Management API endpoints
  the CLI depends on exists (path + verb) in the committed OpenAPI document
  (`spec/management.json`). Guards against endpoint drift without a live instance. See
  [ADR 0001](adr/0001-contract-test-approach.md) and issue #52.
- **Endpoint drift** — when the real Umbraco Management API changes (endpoint moved,
  renamed or removed) such that a URL the CLI calls no longer exists. The original cause
  of the #39/#40/#44 bugs.
- **Hand-written path** *(historical)* — a Management API call made directly through
  `HttpClient` with a URL string literal. Removed by the Kiota migration (#98); every call now
  goes through the generated client.
- **Generated client** — the Kiota-generated request builders and models under
  `src/Umbraco.Cli.Client/Generated`, produced from `spec/management.json` by
  `scripts/regen-client.ps1`.
- **Schema (in the pipeline sense)** — the structural definitions of an Umbraco
  site: document types, media types, member types, data types, and templates. Distinct from
  **content**
  (the documents/media authored against that schema). Schema export/diff/apply is
  ADR 0005; the parallel pipeline for content is ADR 0006. See
  [ADR 0005](adr/0005-schema-export-diff-apply.md).
- **Snapshot** — a single JSON document produced by `schema export` holding the
  verbatim Management-API bodies of every document type, media type, member type, data type
  and template
  (`{ schemaVersion, documentTypes[], mediaTypes[], memberTypes[], dataTypes[], templates[] }`).
  The
  round-trippable, portable representation that `diff` and `apply` consume.
- **Raw-JSON passthrough** — the snapshot fidelity decision: capture each
  entity's verbatim get-by-id body rather than a re-modelled projection, because
  the CLI's own response records are **lossy** (drop doc-type properties/
  compositions, data-type config values, template Razor). See ADR 0005 §1.
- **Lossy record** — a hand-written CLI response record in `Models.cs` that
  projects only a subset of the API body (e.g. `DocumentTypeResponse` omits
  properties). Fine for `list`/`get` display; unusable for a faithful export,
  which is why the pipeline reads raw JSON instead.
- **Identity / matching key** — how `diff`/`apply` pair a snapshot entity with a
  live one. For **schema**: **GUID-primary, alias-fallback** (GUID `id` first; then
  `alias` for doc types/templates, `name` for data types). See ADR 0005 §2. For
  **content**: **GUID-only, no fallback** — documents have no stable natural key (a
  name is per-culture and not unique), so identity is the document id and
  portability depends on it being preserved. See ADR 0006 §2.
- **Placement / parent drift** — content-only. A document's raw body does not carry
  its parent, so a **content snapshot** stores each entry as `{ id, parent, body }`
  (placement captured from the tree). When a live document's body matches the
  snapshot but its parent differs, `content diff` reports it as **`Drifted`** —
  advisory only: apply replaces bodies and creates documents in place, it does not
  move existing documents. See ADR 0006 §1, §4.
- **Scope root** — content-only. The `root` a `content export` was scoped to, stored
  in the snapshot so `diff`/`apply` compare against the **same** live scope. Without
  it a subtree snapshot's `--prune` would treat every out-of-scope document as
  "removed" and delete it. See ADR 0006 §3.
- **Prune** — the opt-in `apply --prune` behaviour of deleting live entities that
  the snapshot matches nothing to. Off by default (apply never deletes without
  it); when on, the run is destructive and needs `--yes`. Schema gates prune on
  `--yes` (ADR 0005 §4); content, being riskier, requires **both** `--prune` and
  `--yes` (ADR 0006 §4).
- **Snapshot format version** — the `schemaVersion` field *inside* a snapshot
  document (currently `"2"`), versioning the snapshot layout. Independent of the
  output envelope's `meta.schemaVersion` (the CLI's JSON contract version).
- **Selector** — the positional `<id>` a command acts on. Always named `id`, it accepts the GUID
  plus the item's natural keys (alias, then name; a language's ISO code; a dictionary item's key).
  See [conventions.md](conventions.md) section 3.
- **Document type vs content** — the noun `content` is the content items (the backoffice section);
  their types are **document types** (`document-type`), never "content types".
- **Mutating / destructive** — every command that sends a write is *mutating* (blocked by
  `--readonly`); a *destructive* one can lose data the CLI cannot restore, or take something
  offline, and needs `--yes` non-interactively. Both are declared per command
  (`.Mutating()`, `.Destructive()`), never inferred from the verb.
- **Write result** — the `data` every write returns: the resulting item for create/update/copy/
  upload, otherwise `{ "id" }` / `{ "ids" }` of what it acted on.
- **Legacy name** — a command name renamed by the #268 surface batch that still runs for one
  release, with a warning (`LegacyNames`).

