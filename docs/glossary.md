# Glossary

Project-specific terms. Add entries as concepts are introduced.

- **Contract test** — a fast unit test that asserts the set of Management API endpoints
  the CLI depends on exists (path + verb) in the committed OpenAPI document
  (`spec/management.json`). Guards against endpoint drift without a live instance. See
  [ADR 0001](adr/0001-contract-test-approach.md) and issue #52.
- **Endpoint drift** — when the real Umbraco Management API changes (endpoint moved,
  renamed or removed) such that a URL the CLI calls no longer exists. The original cause
  of the #39/#40/#44 bugs.
- **Hand-written path** — a Management API call in `UmbracoManagementClient` made directly
  through `HttpClient` with a URL string literal, as opposed to a Kiota-generated request
  builder. These are the drift-risk surface the contract test targets.
- **Generated client** — the Kiota-generated request builders and models under
  `src/Umbraco.Cli.Client/Generated`, produced from `spec/management.json` by
  `scripts/regen-client.ps1`.
- **Schema (in the pipeline sense)** — the structural definitions of an Umbraco
  site: document types, data types, and templates. Distinct from **content**
  (the documents/media authored against that schema). Issue #68's first slice
  covers schema; content is deferred. See [ADR 0004](adr/0004-schema-export-diff-apply.md).
- **Snapshot** — a single JSON document produced by `schema export` holding the
  verbatim Management-API bodies of every document type, data type, and template
  (`{ schemaVersion, documentTypes[], dataTypes[], templates[] }`). The
  round-trippable, portable representation that `diff` and `apply` consume.
- **Raw-JSON passthrough** — the snapshot fidelity decision: capture each
  entity's verbatim get-by-id body rather than a re-modelled projection, because
  the CLI's own response records are **lossy** (drop doc-type properties/
  compositions, data-type config values, template Razor). See ADR 0004 §1.
- **Lossy record** — a hand-written CLI response record in `Models.cs` that
  projects only a subset of the API body (e.g. `DocumentTypeResponse` omits
  properties). Fine for `list`/`get` display; unusable for a faithful export,
  which is why the pipeline reads raw JSON instead.
- **Identity / matching key** — how `diff`/`apply` pair a snapshot entity with a
  live one: **GUID-primary, alias-fallback** (GUID `id` first; then `alias` for
  doc types/templates, `name` for data types). Enables both same-instance
  idempotency and cross-environment portability. See ADR 0004 §2.
- **Prune** — the opt-in `apply --prune` behaviour of deleting live entities that
  the snapshot matches nothing to. Off by default (apply never deletes without
  it); when on, the run is destructive and needs `--yes`. See ADR 0004 §4.
- **Snapshot format version** — the `schemaVersion` field *inside* a snapshot
  document (currently `"1"`), versioning the snapshot layout. Independent of the
  output envelope's `meta.schemaVersion` (the CLI's JSON contract version).
