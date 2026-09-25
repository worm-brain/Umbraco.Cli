# CONTEXT - glossary

The ubiquitous language for this repo. Glossary only - no implementation detail,
no decisions (those live in `docs/adr/`).

## Type reference (document type, media type, member type)

An Umbraco content/media/member type can be named three ways, and the CLI is
careful about which it means:

- **Id** - the type's `Guid`. Globally unique and stable. What the Management API
  create models (`ReferenceByIdModel`) require.
- **Alias** - the developer-facing machine name (e.g. `textPage`). Stable across
  environments; what developers know, and what `<id|alias>` arguments and options
  accept (templates, document/media/member types, user groups).
- **Name** - the human display name (e.g. `Text Page`). May be renamed; not a
  stable identifier. What the type *search* result models expose (they do **not**
  expose Alias). The shared resolver falls back to it after the alias (document
  types excepted).

"Resolve" a reference = turn a caller-supplied **alias, name or key** (or a GUID
passed directly) into the **id** the API needs: alias first, then name, ignoring
case; a name that matches several items is refused (409) rather than guessed.

## Property-editor value

The stored value of a single property on a content/media/member item, keyed by
its property **alias** (e.g. `umbracoFile`, `bodyText`). Arbitrarily shaped JSON
(string, number, boolean, array, or nested object - block-list editors store
large structures). On the generated client it is carried as an `UntypedNode`.

## Hydration

Re-reading a just-created/updated resource by its id (a GET after the write) so
the CLI can return a complete payload (name/url/alias populated), because
Umbraco's create/update responses have an empty body. Best-effort: a failed
hydration GET never fails an already-succeeded write.

## Temporary-file flow

The two-step Umbraco 14+ file upload: POST the file bytes to the `temporary-file`
endpoint (staging), then create the media item referencing that temp-file id
inside a property value. Replaces the older single multipart create.

## Transitional hybrid

The interim state (post-#50, pre-#79) where the client holds both a raw
`HttpClient` (`_http`) and the generated Kiota client (`_api`). #79 removes
`_http`, ending the hybrid - the client becomes fully generated.
