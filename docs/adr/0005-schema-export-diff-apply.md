# ADR 0005: Schema export / diff / apply pipeline

- Status: Accepted
- Date: 2026-07-28
- Issue: #68 (epic: export / import / diff content and schema). This ADR covers the
  **first slice: schema only** (document types, data types, templates). Content
  export/diff/apply is split into follow-up issues.
- Builds on: ADR 0003 (generated write path; client-supplied GUID create).

## Context

Issue #68 pitches the CLI's potential killer feature for CI and agents: dump
content + schema to JSON, diff it against a live instance, and apply the
difference -- complementing uSync but driven from any shell or agent. The first
shippable slice is the **schema** half: document types, data types, templates.

The CLI already has create/update/delete primitives for these (data-type and
template update landed with the mgmt-api-coverage work; this branch stacks on
it). But two facts discovered while scoping shape the whole design:

1. **The hand-written CLI response records are lossy projections.**
   `DocumentTypeResponse` carries no properties/groups/compositions/allowed
   children; `DataTypeResponse` carries no config `values`; `TemplateResponse`
   carries no Razor `content`. Reading through these records would export a
   schema that cannot be recreated. (`src/Umbraco.Cli.Client/Models.cs`.)
2. **There is no document-type update method** on the client -- only create and
   delete. A non-destructive apply of a drifted doc type needs one.

Other relevant facts:

- There is no flat "get all" collection endpoint (issue #39). The only
  enumeration is the paged tree root (`Tree.{DocumentType,DataType,Template}.Root`),
  which returns id + name only. Full enumeration must page over
  `PagedResponse.Total`, then GET each entity by id for its full body.
- `create` accepts a client-supplied GUID `id` (#86), enabling idempotent create
  with a chosen id.
- Writes flow through `MutationInterceptorHandler`, which enforces `--dry-run`
  (capture-and-abort the *first* write) and `--readonly` (block writes). The
  command catalog (`CommandCatalog`) auto-classifies leaf verbs via
  `MutatingVerbs` / `DestructiveVerbs` sets.

## Decision

Ship `umbraco schema export`, `umbraco schema diff <file>`, and
`umbraco schema apply <file>` for document types, data types, and templates.

### 1. Fidelity: raw-JSON passthrough

Export the **verbatim Management-API JSON** of each entity (the full get-by-id
body), not a re-modelled projection. The snapshot is a single JSON document:

```jsonc
{
  "schemaVersion": "1",          // snapshot-format version, independent of the envelope's meta.schemaVersion
  "documentTypes": [ { /* raw GET /document-type/{id} body */ } ],
  "dataTypes":     [ { /* raw GET /data-type/{id} body */ } ],
  "templates":     [ { /* raw GET /template/{id} body */ } ]
}
```

Rationale: it is the only faithful, round-trippable form achievable without a
large typed-modelling effort (the create collections are currently untyped
`IEnumerable<object>`, so typed records would be weeks of work and a fresh drift
surface). Diff is a structural JSON compare; apply sends the JSON back.

Consequence: to read the full bodies we **bypass the lossy records**. New client
methods return the raw body as a `System.Text.Json.Nodes.JsonNode` for each
entity's get-by-id, and a "get all ids" that pages the tree root. Apply posts the
raw JSON via the existing typed create/update requests where their shape is
sufficient, or via a raw-body send where it is not (doc-type properties/
compositions live only in the untyped create collections, so the raw node is
deserialised straight onto `CreateDocumentTypeRequest`'s `object` collections).

### 2. Identity: GUID-primary, alias-fallback

Match a snapshot entity to a live one by GUID `id` first; if no GUID match, fall
back to the business key -- `alias` for document types and templates, `name` for
data types (data types have no alias). Resolution per entity:

- **GUID match** -> UPDATE the live entity in place (by its own id).
- **No GUID match but alias/name match** -> UPDATE the live entity by *its* id
  (not the snapshot's), and record an `idMismatch` note in the diff.
- **No match at all** -> CREATE, reusing the snapshot's GUID for determinism.
- **Live entity matched by nothing in the snapshot** -> a prune candidate.

This is the most flexible option (handles same-instance idempotency and
cross-environment portability) at the cost of the most edge cases, which the
test matrix covers explicitly (guid-only, alias-only, both, neither, and
guid/alias cross-collision).

Data-type caveat: data-type `name` is not guaranteed unique by Umbraco. Duplicate
names in either side are reported as ambiguous and skipped rather than guessed.

### 3. Add a document-type update path

Apply needs to update an existing document type in place (never the destructive
delete+recreate that would cascade to content). Because fidelity is raw-JSON
passthrough (§1), this is a **raw** update — `UpdateDocumentTypeRawAsync(id, body)`
on `ISchemaClient`, PUTting the verbatim body — rather than a typed
`UpdateDocumentTypeRequest`. (A typed doc-type update was the original plan, but a
typed request would re-introduce the lossy-model problem for properties/
compositions; the raw update is consistent with the create/update path for all
three kinds.)

### 4. Apply semantics and safety

- **Default apply = create + update only. It never deletes.**
- **`--prune`** opt-in additionally deletes live entities matched by nothing in
  the snapshot.
- Any run that would delete (only possible with `--prune`) is **destructive**:
  it needs `--yes` non-interactively (the repo's existing confirmation gate) and
  is fully previewable with `--dry-run`.
- **`--dry-run`** computes the diff/plan and prints it, writing nothing, exit 0.
  Apply implements dry-run **at the command level** (short-circuit on
  `ctx.DryRun`) rather than relying on `MutationInterceptorHandler`, whose
  capture-and-abort fires on the *first* write and so cannot preview a
  multi-write plan.
- **`--readonly`** is honoured at the HTTP layer for free: apply does its
  harmless reads (load snapshot, export live, diff) and then the first write
  throws `ReadOnlyModeException`, which the executor maps to a clean error and
  exit 2 — nothing is mutated. (We deliberately do *not* add a bespoke up-front
  `ctx.ReadOnly` check in the command: it would duplicate the interceptor's job
  and add a special-case branch for no behavioural gain.)
- **Apply order respects dependencies:** creates/updates run data types ->
  templates -> document types (doc-type properties reference data types;
  `allowedTemplates` reference templates). Within a kind, **creates** are
  topologically ordered by any same-kind id referenced in the body (captures
  `compositions` and template master references generically); because ids are
  client-supplied and known up front, other references resolve regardless of
  order. A reference cycle (unsupported by Umbraco anyway) falls back to input
  order rather than looping. Deletes run in reverse **cross-kind** order
  (document types -> templates -> data types); within a kind deletes use
  enumeration order (a `Removed` change carries no body to order by), relying on
  fail-fast + re-run when a still-referenced delete is rejected.

### 5. Catalog + command wiring

- New noun `schema` with leaf verbs `export`, `diff`, `apply`.
- `export` and `diff` are **reads** (safe; not added to any verb set).
- `apply` is a **write**: add `"apply"` to `CommandCatalog.MutatingVerbs` and
  `CommandCatalog.DestructiveVerbs` so `--readonly` blocks it and the catalog
  reports it needs `--yes`.
- `export` writes the snapshot to stdout (default) or to a path via `--out`;
  `diff` and `apply` take the snapshot file as a positional argument, or `-` /
  stdin.
- `diff` output follows the repo's list idiom (`RunTableAsync`): a **flat row per
  actionable change** — `kind, change, identity, desiredId, currentId, idMismatch,
  note` — rendered as a table for humans and, in JSON mode, an array of those row
  objects for agents. Unchanged entities are omitted, so an **empty array means no
  drift** (the CI signal). Exit code is 0; a non-empty diff is data, not an error.
  (A future `--exit-code` flag could make drift a non-zero exit; a nested
  `{added,changed,removed}` shape was considered but the flat rows carry the ids
  an agent needs to act and match how every other list command renders. Deferred.)

## Approach: TDD, one slice at a time

Each slice is worked test-first against `FakeUmbracoManagementClient` and
committed independently:

1. **Client full-fidelity reads** -- raw-JSON get-by-id + get-all-ids for the
   three entity types; `UpdateDocumentTypeAsync`. Unit-tested via the fake.
2. **`schema export`** -- enumerate + assemble the snapshot document.
3. **Schema diff engine** -- pure, client-free matcher/differ (GUID-primary,
   alias-fallback) over two snapshot documents. The richest unit-test surface.
4. **`schema diff`** command -- live snapshot vs file, structured + human output.
5. **`schema apply`** command -- plan from the diff engine, ordered writes,
   `--dry-run` / `--prune` / `--yes` / `--readonly` handling.
6. **Catalog + docs** -- register `apply`; README section; live integration
   round-trip (export -> apply to a scratch instance -> re-export -> diff == empty).

## Consequences

- A faithful, round-trippable schema snapshot the CLI could never produce before;
  the lossy records stay for the interactive `list`/`get` commands (unchanged).
- Export costs O(number of schema entities) GET calls (tree pages + one get-by-id
  each); acceptable for a CI/agent tool.
- The diff engine is pure and client-free, so the hardest logic is the
  cheapest to test.
- Content export/diff/apply, cross-version verification, and a `diff --exit-code`
  CI gate are out of scope here and tracked as follow-ups. Content export/diff/apply
  is now implemented as a parallel pipeline in [ADR 0006](0006-content-export-diff-apply.md).
