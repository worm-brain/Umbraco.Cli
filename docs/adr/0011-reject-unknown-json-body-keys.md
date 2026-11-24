# ADR 0011: Reject unknown keys in the schema nouns' --json-body

- Status: Accepted
- Date: 2026-11-24
- Issue: #460. Related: #474 (the typed `--json-body` commands), #201 (the `get` / `update`
  round trip).

## Context

`create` and `update --json-body` on `document-type`, `media-type`, `member-type`, `data-type`
and `template` send the body to the Management API as it is (`RawBodyCommand`). The API's
deserialiser drops keys its model does not have, so a misspelt key (`allowedTemplatez`) or a
wrong nested shape (`[{"template":{"id":...}}]` where the model takes `[{"id":...}]`) changed
nothing, yet the command exited 0 with `"status": "success"`. A script or agent that trusts the
exit code believes a change happened that did not.

The body's JSON Schema is already embedded (`ApiBodySchema`, what `--schema` prints). It is
generated from `spec/management.json`, and every request model in it is closed
(`additionalProperties: false`).

## Decision

### 1. A body key the schema does not define fails before anything is sent

Before any request, the body is walked against the verb's schema: the create schema for
`create`, the update schema for `update` (merge and `--replace` alike). Every key not defined at
its position, at any depth (nested objects and array items included), is collected. If there are
any, the command fails with exit `1`, category `invalid_argument`, and a message listing each
key's JSON path (`$.allowedTemplates[0].template`, `$.allowedTemplatez`) and pointing at
`--schema` and `--allow-unknown-keys`.

Only keys are checked. Values (types, enums, GUID formats, required keys) are left to the server,
which already rejects them with a 400. A free-form schema (a data type's `values[].value`, `{}` in
the spec) has no keys to check.

### 2. The read-only keys `get` returns are accepted at the top level

`get` prints the verbatim response body, and its output (and `--example`'s, which is a `get`)
must stay a valid `--json-body`. The response models have a few top-level keys the request models
lack:

| Kind | Read-only keys |
| --- | --- |
| document type, member type | `id` |
| media type | `id`, `isDeletable`, `aliasCanBeChanged` |
| data type | `id`, `isDeletable`, `canIgnoreStartNodes` |
| template | `id`, `masterTemplate` |

These are accepted at the top level, on create and update, as they were before. The list lives
in `ApiBodySchema.ReadOnlyKeys`; a unit test recomputes it from the spec (the response model's
properties minus the update model's), so it cannot drift. The nested models of the response and
request bodies have the same keys, so nothing below the top level needs an exception. Another
test runs every item of the hands-on fixture (a schema export from a real Umbraco 17 site) through
both checks.

Alternatives rejected:

- **Validate against the union of the request and response schemas.** It needs the response
  models embedded as well, and would also accept response-only keys at any depth if the two ever
  diverged below the top level. A short, spec-checked list is smaller and exact.
- **Strip the read-only keys before sending.** It changes what is sent, for no gain: the server
  ignores them today, and `update` merges into an item that already carries them.

A read-only key is still ignored by the server: changing `masterTemplate` through
`template update --json-body` does nothing, as before. That is a value question, outside this
decision.

### 3. `--allow-unknown-keys` skips the check

The bundled spec is the CLI's, not the site's. A newer Umbraco can add a key the CLI does not
know yet, and the CLI must not stand between a caller and that key. `--allow-unknown-keys` on
those create and update verbs sends the body as it is, unchecked. It needs `--json-body`; given
without one it is a parse error rather than a silently ignored flag (docs/conventions.md 4.5).

## Consequences

- A body with an unknown key, which used to "succeed" doing nothing, now fails. A caller relying
  on that (keys left over from another tool, a body from a newer Umbraco) adds
  `--allow-unknown-keys` or removes the keys.
- `schema apply` is unchanged: it writes bodies the CLI exported itself, through the client
  directly.
- The typed `--json-body` commands (`content`, `media`, `document-blueprint`) drop unknown keys in
  the CLI's own deserialiser. They are tracked separately in #474.
