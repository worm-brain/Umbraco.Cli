# CONTEXT - glossary

The project's shared language, for contributors. Glossary only: no implementation walkthroughs
and no decisions (those live in `docs/adr/`).

## Commands and output

- **Selector** - the positional `<id>` a command acts on. Always named `id`; it accepts the GUID
  and the item's natural keys (alias, then name; a language's ISO code; a dictionary item's
  key). See [conventions.md](docs/conventions.md) section 3.
- **Document type vs content** - the noun `content` is the content items (the backoffice
  section); their types are **document types** (`document-type`), never "content types".
- **Mutating / destructive** - every command that sends a write is *mutating* (blocked by
  `--readonly`); a *destructive* one can lose data the CLI cannot restore, or take something
  offline, and needs `--yes` non-interactively. Both are declared per command (`.Mutating()`,
  `.Destructive()`), never inferred from the verb.
- **Write result** - the `data` every write returns: the resulting item for
  create/update/copy/upload, otherwise `{ "id" }` / `{ "ids" }` of what it acted on.
- **Envelope version** - `meta.schemaVersion` on every JSON result: the version of the CLI's
  output contract. Independent of a snapshot's format version.

## Type references (document, media and member types)

A type can be named three ways:

- **Id** - the type's GUID. Globally unique and stable; what the Management API's create models
  require.
- **Alias** - the developer-facing machine name (e.g. `textPage`). Stable across environments;
  what `<id|alias>` arguments and options accept.
- **Name** - the display name (e.g. `Text Page`). Can be renamed, so not a stable identifier.

**Resolve** a reference = turn a caller-supplied alias, name or key (or a GUID) into the id the
API needs: alias first, then name, ignoring case. A name that matches several items is refused
rather than guessed.

## Environment sync

- **Schema** - a site's structural definitions: document, media and member types, data types,
  templates, languages, dictionary items, member and user groups, and the static files
  templates render (partial views, stylesheets, scripts). Distinct from **content**, the
  documents and media authored against it.
- **Snapshot** - the portable file `schema export` or `content export` writes, holding each
  entity's verbatim Management API body. What `diff` and `apply` consume.
- **Media snapshot** - the directory `media export` writes: `media.json` (each item's body,
  parent and file entry, in tree order) plus `files/<id>/<name>`. A directory because the files
  are binary.
- **Raw-JSON passthrough** - capturing each entity's verbatim get-by-id body rather than a
  re-modelled projection, so nothing is lost on the round trip.
- **Lossy record** - a CLI response record in `Models.cs` that projects only part of an API body
  (e.g. `DocumentTypeResponse` omits properties). Fine for `list`/`get` output; not used for
  snapshots.
- **Absent section** - a snapshot section that is missing (or null) is not managed: `diff` and
  `apply` skip that kind and `--prune` deletes none of it. A present section is the whole list
  for its kind.
- **Identity / matching key** - how `diff` and `apply` pair a snapshot entity with a live one.
  Schema: GUID first, then alias (name for data types). Content: GUID only, because documents
  have no stable natural key.
- **Placement / parent drift** - content only. A content snapshot stores each entry as
  `{ id, parent, body }`. When a live document's body matches but its parent differs,
  `content diff` reports it as **Drifted**; `apply` does not move existing documents.
- **Scope root** - content only. The `root` a `content export` was scoped to, stored in the
  snapshot so `diff` and `apply` compare against the same live subtree.
- **Prune** - the opt-in `apply --prune`: delete live entities the snapshot has no match for.
  Off by default; when on, the run is destructive and needs `--yes`.
- **Snapshot format version** - the `schemaVersion` field inside a snapshot file (schema
  snapshots are currently `"4"`, content and media `"1"`). Independent of the envelope version.

## Talking to Umbraco

- **Generated client** - the Kiota request builders and models under
  `src/Umbraco.Cli.Client/Generated`, produced from `spec/management.json` by
  `scripts/regen-client.ps1`. Every Management API call goes through it.
- **Contract test** - a fast unit test asserting that every endpoint the CLI depends on exists
  (path + verb) in the committed OpenAPI document. Catches **endpoint drift** (an endpoint moved,
  renamed or removed upstream) without a live site.
- **Guarded passthrough** - `umbraco api <method> <path>`: one request, with a caller-chosen
  method and path under `/umbraco/`, sent through the same pipeline and guardrails as every
  command. Not to be confused with the raw-JSON passthrough above.
- **Extension command** - a noun a package adds by shipping an `umbraco-<noun>` executable, which
  the CLI runs from PATH when the noun is not one of its own. **Context options** are the global
  options it hands to the extension's calls through `UMBRACO_*` variables.
- **Property-editor value** - the stored value of one property on a content, media or member
  item, keyed by the property alias. Arbitrary JSON; carried as an `UntypedNode` on the
  generated client.
- **Hydration** - re-reading a resource by id after a write, so the CLI can return the full item
  (Umbraco's create/update responses have an empty body). Best-effort: a failed read never fails
  a write that succeeded.
- **Temporary-file flow** - how files are uploaded: POST the bytes to the `temporary-file`
  endpoint, then create or update the media item referencing that temp-file id in a property
  value.
