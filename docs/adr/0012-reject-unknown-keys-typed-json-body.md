# ADR 0012: Reject unknown keys in the typed --json-body commands

- Status: Accepted
- Date: 2026-11-24
- Issue: #474. Extends ADR 0011 (#460), which left these commands out.

## Context

ADR 0011 made the schema nouns' `--json-body` fail on a key its schema does not define. The
commands that read `--json-body` into the CLI's own request records were left out:

| Command | Request record |
| --- | --- |
| `content create` | `CreateContentRequest` |
| `content update` | `UpdateContentRequest` |
| `media update` | `UpdateMediaRequest` |
| `document-blueprint create` | `CreateDocumentBlueprintRequest` |
| `document-blueprint update` | `UpdateDocumentBlueprintRequest` |

Their bodies are deserialised with System.Text.Json, which skips a key the record lacks. A
misspelt key (`valuez`, `variants[0].nmae`) was lost inside the CLI, before anything was sent, and
the command still reported success.

## Decision

### 1. The same rule, through the same check

Before any request, the body's keys are checked at every depth, by the same walker that ADR 0011
uses (`JsonBodyKeys`). An unknown key fails with exit `1` and `invalid_argument`. The message
lists each key's JSON path in document order and points at `--schema` and
`--allow-unknown-keys`.

The schema checked against is the one `--schema` prints (`JsonBodySchema`, exported from the
request record). Every record in it is now closed: `additionalProperties: false`, so `--schema`
states the rule. A free-form property value (`values[].value`) has no keys to check.

### 2. `get` output stays a valid body

The output of `get`, edited and written back, must keep working. So must the bodies the docs pipe
from one command into another. So the body is checked against the request record **and** the
record the matching read prints, and a key is unknown only where neither defines it, at its own
position:

| Command | Also accepted |
| --- | --- |
| `content create`, `content update` | `content get`'s shape (`ContentItemResponse`) |
| `document-blueprint create`, `update` | the same `ContentItemResponse` shape |
| `media update` | `media get`'s shape (`MediaItemResponse`) |

`document-blueprint get` and `scaffold` print the Management API's `DocumentBlueprintResponseModel`
verbatim. Its keys are covered by `ContentItemResponse`, position by position. A unit test builds
a body with every key of that model from `spec/management.json` and checks it against the
document bodies. `content create --example` output passes too (its own test reads it through
`content create`).

This is the union that ADR 0011 rejected for the schema nouns, and here it fits. There, the extra
keys sat only at the top level, and the response models would have had to be embedded. Here, the
read shapes are CLI records already in the binary, and their extras sit at nested positions too
(`variants[].state`, `values[].editorAlias`). A key from a read shape is accepted only where that
shape has it: `urls` passes at the top level but not inside a variant.

A body written for one command is not accepted by another just because they look alike. For
example, `content update` refuses `contentType`: `content get` prints `documentType`, and an update
cannot change a document's type.

### 3. What `--allow-unknown-keys` does here

These verbs take `--allow-unknown-keys` too, and it skips the check. Unlike the schema nouns, it
cannot send the unknown keys: the body is read into the CLI's request record, which has nowhere
to put them. Here the flag means "ignore them instead of failing", which is the old behaviour,
and its help text says so. Across all the commands that have the flag, it means the same thing:
skip the unknown-key check. It is still a parse error without `--json-body`.

## Consequences

- A typed body with an unknown key, which used to "succeed" while ignoring that key, now fails.
- `--schema` on these five commands now shows `additionalProperties: false` on every object.
- ADR 0011's walker moved to `JsonBodyKeys`, shared by both kinds of body. Its behaviour for the
  schema nouns is unchanged: their read-only keys are a second, top-level-only schema.
