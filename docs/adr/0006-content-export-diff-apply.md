# ADR 0006: Content export / diff / apply pipeline

- Status: Accepted
- Date: 2026-09-16
- Issue: #100 (content half of #68). Builds directly on ADR 0005 (schema export/diff/apply).

## Context

ADR 0005 gave the schema half of #68 a raw-JSON export/diff/apply pipeline. #100 is the content
half: `content export`, `content diff`, `content apply` over documents. The intent is a portable,
CI/agent-friendly snapshot that can be diffed and applied across environments - the killer feature
being cross-environment content migration and drift detection.

Content differs from schema in four ways that shaped this design, so it warrants its own ADR rather
than a footnote on 0005.

## Decision

Mirror the schema pipeline's structure (snapshot -> exporter -> pure diff engine -> ordered applier,
plus a shared file loader and diff prelude) and reuse the entity-agnostic raw-JSON transport helpers
(`GetRawJsonAsync`/`SendRawJsonAsync`). The content-specific decisions:

### 1. Snapshot stores placement alongside each body

Unlike a schema entity, a document's raw `GET /document/{id}` body does **not** carry its parent -
placement lives in the tree, not the entity. So a `ContentNode` is `{ id, parent, body }`: the
verbatim body plus the parent captured during the tree walk. Documents are stored in tree pre-order
(parents before children). On apply, `create` injects the captured parent back into the POST body
(`{ "parent": { "id": ... } }`, or `null` at the content root); the raw body already carries its own
`id`, so identity is preserved.

### 2. GUID-primary identity, no fallback

Schema entities fall back to an alias/name when GUIDs differ. Documents have no stable natural key
(a name is per-culture and not unique), so content matching is **GUID-only**. Cross-environment
portability therefore depends on the document id being preserved, which apply does by creating with
the snapshot's own GUID (Umbraco 14+ accepts a client-supplied id).

### 3. The snapshot is scope-self-describing (prune safety)

`content export --root <id>` scopes the dump to a subtree. If diff/apply then exported the *whole*
live tree to compare, every document outside the subtree would classify as "removed" and `--prune`
would delete it - a catastrophic footgun. So the snapshot records its `root`, and diff/apply export
the live tree at that **same** root. Prune therefore only ever considers documents within the
snapshot's own scope.

### 4. Prune is double-gated; moves are out of scope

Deleting live content is more dangerous than deleting schema, so `content apply` creates/updates by
default and requires **both** `--prune` and `--yes` to delete (schema's model gates prune on `--yes`
alone). Deletes run deepest-first (reverse pre-order) so a parent is not removed while it still has
children. Apply replaces document bodies and creates new documents in place; it does **not**
re-parent existing documents - a placement drift is surfaced by `diff` (a `parentDrift` flag) but a
move is deliberately out of scope for this pass.

## Consequences

- A snapshot is a faithful, human-diffable backup/migration artifact for a content subtree, with
  stable identity across environments.
- **Property-value references are not rewritten.** A document's values may reference other content,
  media, or data types by GUID; those travel verbatim, so a referenced item must already exist in
  the target or the referencing document will fail to apply. Rewriting references is out of scope.
- **The raw GET -> POST/PUT round-trip is unverified against a live instance.** As with the schema
  pipeline, the write side assumes Umbraco accepts a document's own GET body (plus an injected
  parent) back on create/update. The document response and request models diverge more than the
  schema ones (response variants carry state/dates the request may not accept), so this is a real
  risk. The pipeline's mechanics are unit-tested; live round-trip fidelity is tracked as a follow-up
  and belongs in the deferred integration harness (#51/#77).
