# Command reference

Every command in the `umbraco` CLI, grouped by what it works on. To install and sign in, see
[getting-started.md](getting-started.md). For the JSON output, exit codes and guardrails that
every command shares, see [agent-guide.md](agent-guide.md).

> `umbraco commands` prints the whole command tree as JSON, straight from the code (including a
> `destructive` flag per command), so it's always up to date. If it and this page ever disagree,
> trust `umbraco commands` - and please
> [open an issue](https://github.com/worm-brain/Umbraco.Cli/issues).

## Contents

- [Global options](#global-options)
- [Discovery: `commands` and `--schema`](#discovery-commands-and---schema)
- [Shell completion: `completion`](#shell-completion-completion)
- [`auth`](#auth)
- [`content`](#content)
- [`document-blueprint`](#document-blueprint)
- [`media`](#media)
- [`media-type`](#media-type)
- [`document-type`](#document-type)
- [`data-type`](#data-type)
- [`language`](#language)
- [`template`](#template)
- [`member`](#member)
- [`member-type`](#member-type)
- [`member-group`](#member-group)
- [`user`](#user)
- [`user-group`](#user-group)
- [`user-data`](#user-data)
- [`dictionary`](#dictionary)
- [`webhook`](#webhook)
- [`script` / `stylesheet` / `partial-view`](#script--stylesheet--partial-view-static-files)
- [`tag` / `culture`](#tag--culture-read-only)
- [`server`](#server-read-only)
- [`health`](#health)
- [`log-viewer`](#log-viewer)
- [`models-builder`](#models-builder)
- [`manifest`](#manifest-read-only)
- [`redirect`](#redirect)
- [`relation-type` / `relation`](#relation-type--relation-read-only)
- [`indexer` / `searcher`](#indexer--searcher)
- [`imaging`](#imaging-read-only)
- [`property-type`](#property-type-read-only)
- [`schema` (export / diff / apply)](#schema-export--diff--apply)
- [`content` (export / diff / apply)](#content-export--diff--apply)

---

## Global options

Available on every command:

| Option | Description |
|---|---|
| `--host <url>` | Umbraco instance base URL (overrides config). Stored or `UMBRACO_CLIENT_*` credentials are only sent to their own host, so a `--host` naming another instance needs `--token` (see [Where credentials are sent](#where-credentials-are-sent)). |
| `--token <bearer>` | Raw bearer token (overrides stored credentials). |
| `--output json\|human\|csv` | Output format. Default: `json` when piped, `human` in a terminal. `csv` is RFC-4180 and only ever explicit. |
| `--quiet`, `-q` | Suppress the result of writes (the confirmation and its `data`), so a successful write prints nothing; reads, `health run` results, errors, `--dry-run` previews, a bulk run with failures, and exit codes still emitted. |
| `--verbose` | Log each HTTP request and response to stderr: method, URL, headers, status, the request body and the first 4 KB of the response body (marked truncated beyond that). Also logs the token exchange and the `auth login` / `auth doctor` requests. Secrets are redacted: string values of any header, JSON property, form field or query parameter named like a password, secret, token, API key, authorization, cookie, credential, private key, passphrase, connection string or session id (separators ignored, so `X-Api-Key` matches); every webhook header value; the value of an `{alias, value}` pair with such an alias; and `user:pass@` in URLs. Numbers, booleans and dates are kept. Binary and multipart bodies are summarised by type and size. |
| `--dry-run` | On a write command, print the request that would be sent (method, URL, body) and exit `0` without executing. A write that takes several requests (`user create --password`, `user update` with several changes) lists the later ones in order under `data.then`. Secrets are redacted as for `--verbose`. No effect on reads. |
| `--yes`, `-y` | Skip the confirmation prompt on destructive commands. **Required** to run one non-interactively. |
| `--readonly` | Block all writes for this session; reads still work. Also `UMBRACO_READONLY=1`. |
| `--fields <a,b>` | Trim JSON output (or CSV columns) to these top-level fields, in order. |
| `--profile <name>`, `-p` | Named credential profile (see [`auth`](#auth)); also `UMBRACO_PROFILE`. |
| `--config <path>` | Path to the config file. |

Colour in human output is disabled when `NO_COLOR` is set (any value) or when stdout is not a
TTY.

### Paging applies to every `list`

Paged `list` commands return at most `--take` items, **default 100** on every one, and say so
in `meta`:

```json
"meta": { "total": 237, "skip": 0, "take": 100, "hasMore": true }
```

In a terminal a truncated list also prints a hint to stderr. Page with `--skip`/`--take` until
`hasMore` is false, or pass `--all` to have the CLI do it.

`--all` reads page after page until the collection is exhausted and returns the whole list, with
`meta` saying so (`total` is the item count, `skip: 0`, `hasMore: false`). It stops at 10,000
items with an `invalid_argument` error rather than returning a truncated list, and it cannot be
combined with `--skip` or `--take` (a parse error, not a silent precedence):

```bash
umbraco data-type list --all
```

`total` and `hasMore` are **omitted when the source cannot count** - an absent `hasMore` means
"unknown", not "no", so do not read a missing `total` as a complete list.

---

### Lists and references work the same everywhere

- **A list option takes commas, spaces or repeats.** `--culture en-US,da-DK`,
  `--culture en-US da-DK` and `--culture en-US --culture da-DK` are the same. This holds for
  every option that takes several values (`--culture`, `--order`, `--group`, `--user`,
  `--section`, `--fallback-permission`, `--exclude-type`, `--exclude-root`, `--level`, `--event`)
  - but not for the `key=value` options (`--value`, `--value-file`, `--domain`), whose values may
  contain a comma.
- **Several targets known up front are positional:** `user-group delete a b c`,
  `imaging resize-urls <id> <id>`.
- **An item can be named instead of given by id** wherever the syntax below says `<id|alias>`,
  `<id|name>` or `<id|key>`. An alias is matched first, then a name, ignoring case (document types
  match their alias only). A reference that matches nothing, or a name that matches several items,
  is an `invalid_argument` error; the latter lists each match's id.
- **Two inputs for one value are refused,** never silently resolved: `--content` with
  `--content-file`, a field flag beside `--json-body` on `content create` /
  `document-blueprint create`, or one ISO code in both `--value` and `--value-file` on
  `dictionary create` / `update`. Stdin is read once, so only one input can be `-`.
- **Every write returns `data`:** `create`/`update`/`copy`/`upload` return the resulting item, other
  writes `{ "id": ... }` (or `{ "ids": [...] }`) of what they acted on.
- **`update` merges:** omitted options keep their values; `--replace` (where offered) sends the item
  as given, is destructive, and needs `--yes` non-interactively.

## Discovery: `commands` and `--schema`

```bash
umbraco commands                        # entire command tree as JSON (local; no host/auth)
umbraco commands --output human         # same, as an indented outline
umbraco content create --schema         # JSON Schema of a command's --json-body (local; no host/auth)
umbraco document-type create --example  # a real document type from the instance, to start from (needs a host)
```

`--schema` means the same on every command that takes `--json-body`: the body's JSON Schema,
printed offline. `--example` (on the document, media, member and data type verbs) prints a real
item instead - the most useful starting point for a body the schema cannot fully describe. On
`content create` it takes `--document-type` and prints a create body with an example value for
every property of that type (see [Property value formats](#property-value-formats-for---json-body)).

In the catalog every option and argument carries its `default` (when it has one) and, when it is
required only without some other option, `requiredUnless` (the options that make it
unnecessary). A `--json-body` command carries `jsonBodySchema`, the command line that prints its
body's schema. Every command with help examples lists them in `examples`.

See [agent-guide.md](agent-guide.md#1-discover-the-surface-umbraco-commands) for details.

---

## Shell completion: `completion`

```bash
umbraco completion bash                 # prints a bash completion script
umbraco completion zsh                  # prints a zsh completion script
umbraco completion pwsh                 # prints a PowerShell completion script
```

Tab-completes nouns, verbs, options and fixed option values. The script asks the installed CLI
for suggestions each time, so it keeps up when you upgrade and needs nothing else installed. It
runs offline - no host or auth. It runs the command as you typed it (`./umbraco`, a path to a
build), not whichever `umbraco` is first on `PATH`; to complete a wrapper script too, register
the same function for its name (the script's header shows how).

| Shell | Install |
|---|---|
| bash | add `eval "$(umbraco completion bash)"` to `~/.bashrc` |
| zsh | add `eval "$(umbraco completion zsh)"` to `~/.zshrc` after `compinit`, or save the output as `_umbraco` in a directory on `$fpath` |
| PowerShell | add `umbraco completion pwsh \| Out-String \| Invoke-Expression` to `$PROFILE` |

---

## `auth`

```bash
umbraco auth login [--host <url>] [--client-id <id>] [--client-secret <secret>]
umbraco auth logout [--profile <name>]        # clears credentials; preserves the profile's allow-list; logging out of the default leaves no default
umbraco auth whoami                           # the resolved identity
umbraco auth doctor [--output json]           # diagnose host/TLS/credentials/auth/identity/version
umbraco auth profile list                     # list credential profiles; "default": true marks the default (* in the table)
umbraco auth profile use <profile>             # make a profile the default
```

`auth doctor` runs a sequence of checks (host resolution, connectivity/TLS, credentials,
authentication, resolved identity, instance version, supported version), reports each as
`pass`/`fail`/`warn`/`skip` with a remediation hint, and exits `1` if any check hard-fails
(warnings do not fail the run). The supported-version check warns, naming both versions, when the
instance's Umbraco major is outside the range this build was tested against (currently 17.x-18.x).
Run it first in any new environment. See [getting-started.md](getting-started.md#5-confirm-it-works).

### Where credentials are sent

Every command that talks to Umbraco, `auth login` and `auth doctor` apply two rules before any
credential (a client secret or a bearer token) leaves the machine:

- **HTTPS only, except loopback.** A host with `http://` is refused unless it is `localhost`,
  `127.0.0.1` or `::1`; use the instance's `https://` URL. There is no opt-out. Commands abort
  with exit `2` and category `refused`; `auth login` reports an authentication failure, and
  `auth doctor` fails its host check.
- **Credentials stay with their host.** A profile's client id and secret belong to the profile's
  host, and `UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET` belong to the host they resolve with
  (`UMBRACO_HOST`, or the profile's host). A `--host` that names a different instance does not get
  them: the command aborts with exit `2` and category `refused` (`auth doctor` fails its
  credentials check) instead. To target another instance, pass `--token` with `--host`, use a
  profile logged in to that host (`--profile`), or set `UMBRACO_HOST` with its own client
  credentials. A `--host` that matches the configured host (ignoring case and a trailing slash) is
  fine.

## `content`

```bash
umbraco content list [--parent <id>] [--trashed] [--skip <n>] [--take <n>] [--all]   # --trashed: the recycle bin
umbraco content tree [--parent <id>] [--recursive] [--depth <n>]   # flat walk; each row carries depth + parentId (cap 50)
umbraco content find --name <text> | --path <a/b/c> [--parent <id>] # locate by name (server search) or by name path
umbraco content get <id>                                   # every field of GET /document/{id} (documentType, values, variants, schedule dates, isTrashed, flags) plus name, parent, urls
umbraco content create --document-type <alias> --name <name> [--culture <code>] [--parent <id>] [--id <guid>] [--template <alias|id>]   # no --culture on a variant type: the default language
umbraco content create --json-body <file> [--id <guid>] [--template <alias|id>]   # the body carries type, name, parent and culture
umbraco content create --example --document-type <alias> [--name <name>] [--culture <code>]   # prints a create body with a value per property; needs a host
umbraco content update <id> [--json-body <file>] [--replace] [--template <alias|id>]   # merges by default; --replace needs --yes
umbraco content delete <id>                                # permanent; needs --yes non-interactively
umbraco content publish <id> [--culture <csv>] [--publish-at <ts>] [--unpublish-at <ts>]   # ISO 8601 to schedule; no --culture publishes every culture the item has
umbraco content unpublish <id> [--culture <csv>]          # takes offline; needs --yes; no --culture = every culture; data.cultures lists the ones that were live (null if invariant)
umbraco content version list <id> [--culture <code>]           # version history; no --culture = every culture, rows tagged `culture`
umbraco content version get <id>                           # one version (its id from version list), values included
umbraco content version rollback <id> [--culture <code>] [--publish]   # draft only unless --publish; see below
umbraco content trash <id>                                 # move to recycle bin (reversible)
umbraco content restore <id> [--parent <id> | --to-root] [--publish]   # comes back unpublished, last in sort order; default = original parent
umbraco content empty-recycle-bin                          # permanent; needs --yes; see it first with list --trashed
umbraco content move <id> [--parent <id>]                  # --target works too
umbraco content sort [--parent <id>] (--order <id>,<id>... | --by name|createDate|updateDate|publishDate [--desc])   # reorder a parent's children
umbraco content copy <id> [--parent <id>] [--include-descendants] [--relate]   # returns the copy, with its new id
umbraco content publish-descendants <id> [--culture <csv>] [--include-unpublished] [--wait]   # --wait polls to completion
umbraco content export [--root <id>] [--out <file>]        # dump subtree/site to a snapshot
umbraco content diff <snapshot>                            # diff a snapshot vs live (read-only)
umbraco content apply <snapshot> [--no-state] [--prune [--exclude-type <alias|id>]... [--exclude-root <id>]...] [--dry-run]   # reconcile bodies + publish state; --prune deletes, needs --yes

# Bulk ops over many ids (from --file or stdin), with a per-item results array:
umbraco content bulk delete [--file ids.txt]               # permanent; needs --yes
umbraco content bulk publish [--file ids.txt] [--culture <csv>]
umbraco content bulk unpublish [--file ids.txt] [--culture <csv>]   # takes offline; needs --yes; no --culture = every culture

# domain sub-noun (Culture and Hostnames):
umbraco content domain get <id>
umbraco content domain set <id> [--default-culture <iso>] [--domain host=iso ...] [--replace]   # merges by hostname; host= removes one; --replace needs --yes
```

`content get` returns the document under the Management API's own keys - `documentType` (the key
the create body and `content version get` use), `values`, `variants` with their `id`, `flags`,
`state` and `scheduledPublishDate` / `scheduledUnpublishDate`, `template`, `isTrashed`, `flags` -
plus what the CLI reads elsewhere: `name` (the first variant's), `parent` (left out at the root)
and `urls` (`[{culture, url}]`, from `GET /document/urls`). `content list` and `content find` rows
use the same keys, but carry only what the tree and search endpoints return: no `updateDate`,
`values`, `urls` or full `variants`. Read the item with `content get` for those.

### Rollback and restore leave the live site alone

Both follow Umbraco's model, which isn't quite "undo":

- **Rollback only changes the draft.** The item shows as `PublishedPendingChanges` and the live
  site keeps serving the published version until you publish. Pass `--publish` to publish the
  document straight after: the rolled-back culture with `--culture`, otherwise every culture it
  has. If the publish fails, the rollback has still happened; the error says so.
- **Pick the version from the flags, not the date.** `content version list` marks the current
  draft (`isCurrentDraftVersion`, the `Draft` column) and the current published version
  (`isCurrentPublishedVersion`, `Published`). The two can carry the same `versionDate`, and
  rolling back to the published one is a no-op when the bad edit was already published: pick
  the newest row older than both.
- **Restore brings an item back unpublished, last in its parent's sort order.** Pass `--publish`
  to publish it (every culture it has) and use `content sort` to put it back in place.
  `media restore` also appends to the end; media has no publish state, so it has no
  `--publish`.

```bash
umbraco content version list <id> --culture en-US          # find the row older than Draft/Published
umbraco content version rollback <version-id> --culture en-US --publish
umbraco content restore <id> --publish
```

### Domains: a multilingual site is not reachable without them

Publishing a document in several cultures does not make those cultures reachable. Umbraco needs
a **domain binding** per culture, and without one it logs "the root node was published with
multiple cultures, but no domains are configured" and serves nothing but the default:

```bash
umbraco content domain set <root-id> --default-culture en-US \
  --domain example.com=en-US --domain example.com/da=da-DK
```

The API replaces the whole set, so `--domain` **merges** into what is already bound, matched on
hostname. Pass `--replace` to set exactly what you name and drop the rest. Each binding must be
`host=isoCode`; a value with no `=` is refused.

### How `content update` writes

`update` **merges** into the item. Values are matched on alias + culture + segment and variants
on culture + segment, so a body that mentions one property changes that property and leaves the
rest alone; the item's template is carried through untouched.

```bash
# adds a Danish title; the English title, the body text and the template all survive
echo '{"values":[{"alias":"title","culture":"da-DK","value":"Hej"}],
       "variants":[{"culture":"da-DK","name":"Hej"}]}' \
  | umbraco content update <id> --json-body -
```

Pass **`--replace`** when you do want the body to stand alone: the item's values and variants are
replaced wholesale and anything absent is cleared. The template still survives `--replace` - use
`--template` to change it.

To change only the template, pass `--template` on its own - no body is needed, and every value
is left as it is: `umbraco content update <id> --template blogPost`. (`--replace` still needs a
body, since an empty one would clear every value.)

### Known sharp edges on `content` (Umbraco 17.x)

These come from the Management API itself.

| What | Effect | Do this instead |
|---|---|---|
| `content list` rows have no **`updateDate`** | The tree endpoint behind `list` does not return it; `content get` does | `umbraco content get <id>` for the items whose date matters |

A trap worth knowing when fixing templates in bulk: a change that touches **only** the template
does not mark culture variants as having pending changes, so `publish-descendants` skips them as
already published. Publish the affected cultures explicitly.

Bulk commands read ids from `--file` or stdin, and take the CLI's own output as it is, so you
can pipe a list straight in - no `jq` or `ConvertFrom-Json` needed:

```bash
umbraco content list --fields id | umbraco content bulk publish            # the JSON envelope
umbraco content list --fields id -o csv | umbraco content bulk publish     # CSV with an id column
```

The format is read from the content: JSON (starts with `{` or `[`) is the success envelope (the
`id` of each `data` item), an array of objects with `id`, or an array of id strings; CSV is
recognised by a header row with an `id` column, which is the column used; anything else is one
id per line. JSON items without an `id`, or a CSV row with an empty `id`, refuse the whole batch
as `invalid_argument` before anything runs. A bulk run's own results carry `id`, so they pipe into
the next bulk command too.

Each id is reported independently in the `data` results array (`{id, status, error}`); the exit
code is `1` if any item failed. A bulk `delete` is gated by a single confirmation (`--yes`
non-interactively) - it never prompts per item.

All create commands accept `--id <guid>` for **idempotent creates** (Umbraco keeps the id you
supply), so re-running a provisioning script doesn't create duplicates.

### Property value formats for `--json-body`

`content create --schema` types `values[].value` as "any", because the shape depends on the
property editor behind each property, not on the CLI. The quickest way to a correct body is to
let the CLI read the document type and print one:

```bash
umbraco content create --example --document-type blogPost -o json | jq .data > body.json
# edit the values, then:
umbraco content create --json-body body.json
```

`--example` reads the document type (and the types it composes) and each property's data type,
and writes one `values[]` entry per property with the example below for its editor. An editor
not in the table (a block editor, a package's own) gets `"value": null`. Every entry carries the
`editorAlias` it was chosen by; `content create` ignores that key, so the file can go straight
back in. On a type that varies by culture the variant and the varying values get `--culture`, or
the default language. `--name` names the variant. Where the table shows `<new guid>` (an id the
caller need not choose), `--example` writes a fresh GUID, so it can be sent as it is. The other
`<...>` placeholders (`<media id>`, `<document id>`) name an item only you know: `content create`
refuses the body, naming the property, until you replace them or remove the entry.

The table is the one `--example` uses (Umbraco 17.7.0):

| Editor | `editorAlias` | `value` shape |
|---|---|---|
| Textstring | `Umbraco.TextBox` | `"some text"` |
| Textarea | `Umbraco.TextArea` | `"some text"` |
| Rich text (Tiptap) | `Umbraco.RichText` | `{"markup":"<p>some text</p>","blocks":null}` |
| Image media picker | `Umbraco.MediaPicker3` | `[{"key":"<new guid>","mediaKey":"<media id>","mediaTypeAlias":"Image","crops":[],"focalPoint":null}]` |
| Date picker | `Umbraco.DateTime` | `"2026-05-01 00:00:00"` |
| Dropdown | `Umbraco.DropDown.Flexible` | `["News","Opinion"]` |
| Tags | `Umbraco.Tags` | `["umbraco","cli"]` |
| Numeric | `Umbraco.Integer` | `5` |
| True/false | `Umbraco.TrueFalse` | `true` |
| Content picker | `Umbraco.ContentPicker` | `"<document id>"` |
| Block List | `Umbraco.BlockList` | an object: see [Block List and Block Grid](#block-list-and-block-grid) |
| Block Grid | `Umbraco.BlockGrid` | an object: see [Block List and Block Grid](#block-list-and-block-grid) |

`key` on a media picker entry is the **picker entry's own** new GUID, not the media item's -
`mediaKey` carries the media id. `--example` generates it; by hand, use a fresh one per entry
(`[guid]::NewGuid()`, `uuidgen`). A dropdown's values must be
among the data type's configured items (`data-type get <id>`).

To look an editor up by hand, `umbraco schema export` has it
(`.documentTypes[].properties[].dataType` -> `.dataTypes[].editorAlias`).

A full value entry carries the property alias and, on a variant document, the culture:

```json
{
  "values": [
    { "alias": "title", "culture": "en-US", "segment": null, "value": "Hello" },
    { "alias": "title", "culture": "da-DK", "segment": null, "value": "Hej" }
  ],
  "variants": [
    { "name": "Hello", "culture": "en-US", "segment": null },
    { "name": "Hej",   "culture": "da-DK", "segment": null }
  ],
  "template": { "alias": "blogPost" }
}
```

`template` takes either an `alias` or an `id` (the id wins if both are given). Omit it on an
update to keep the item's current template; on a create, omitting it uses the document type's
default. The `--template` flag overrides whatever the body says.

A property that does not itself vary by culture takes `culture: null` even on a document that
does. If a value is rejected or silently ignored, read the document back from the Management API
and copy the `culture`/`segment` pairing it reports.

### Block List and Block Grid

A block editor needs three things: an **element type** for each kind of block, a **data type**
that lists the allowed blocks, and a **property value** that holds the blocks themselves. The
Block List shapes below are checked against Umbraco 17.7.0.

**1. Element type.** A normal `document-type create --json-body` with `"isElement": true`. Its
properties are the block's fields.

**2. Data type** (`editorAlias` `Umbraco.BlockList`, `editorUiAlias`
`Umb.PropertyEditorUi.BlockList`), via `data-type create --json-body`:

```json
{
  "name": "Page Sections",
  "editorAlias": "Umbraco.BlockList",
  "editorUiAlias": "Umb.PropertyEditorUi.BlockList",
  "values": [
    { "alias": "blocks", "value": [
      { "contentElementTypeKey": "<element type id>", "label": "{umbValue: title}",
        "editorSize": "medium", "forceHideContentEditorInOverlay": false }
    ] },
    { "alias": "validationLimit", "value": { "min": 0, "max": 10 } },
    { "alias": "useSingleBlockMode", "value": false },
    { "alias": "useLiveEditing", "value": false },
    { "alias": "useInlineEditingAsDefault", "value": false }
  ]
}
```

Add `"settingsElementTypeKey": "<element type id>"` to a block to give it settings.

**3. Property value.** The `value` of the property's entry in `values[]`:

```json
{
  "layout": { "Umbraco.BlockList": [ { "contentKey": "<k1>" } ] },
  "contentData": [
    { "key": "<k1>", "contentTypeKey": "<element type id>",
      "values": [ { "alias": "title", "value": "Hello", "culture": null, "segment": null } ] }
  ],
  "settingsData": [],
  "expose": [ { "contentKey": "<k1>", "culture": null, "segment": null } ]
}
```

- `key` / `contentKey` is a **new GUID per block**, generated by you. `layout` sets the order;
  `contentData` holds the fields.
- **A block missing from `expose` is not published.** Add one `expose` entry per block and per
  culture it should appear in.
- On a variant property, send one value entry per culture, each with its own blocks. The
  element's own values take `culture: null` when the element type is invariant.
- With settings, add `"settingsKey": "<s1>"` to the layout item and a matching
  `{ "key": "<s1>", "contentTypeKey": "<settings type id>", "values": [...] }` to `settingsData`.

**Block Grid** (`Umbraco.BlockGrid`, `Umb.PropertyEditorUi.BlockGrid`) works the same way,
with a grid added on top. The data type's `blocks` entries also carry layout rules, and there
are optional `blockGroups` and `gridColumns` (12 by default):

```json
{ "alias": "blocks", "value": [
  { "contentElementTypeKey": "<element type id>", "allowAtRoot": true, "allowInAreas": false,
    "columnSpanOptions": [], "rowMinSpan": 1, "rowMaxSpan": 1, "editorSize": "medium",
    "areas": [ { "key": "<area guid>", "alias": "main", "columnSpan": 12, "rowSpan": 1,
                 "minAllowed": 0, "specifiedAllowance": [] } ] }
] }
```

Its value uses `"layout": { "Umbraco.BlockGrid": [...] }`, and each layout item carries its size
and any nested areas:

```json
{ "contentKey": "<k1>", "settingsKey": null, "columnSpan": 12, "rowSpan": 1,
  "areas": [ { "key": "<area guid from the data type>", "items": [ { "contentKey": "<k2>", "settingsKey": null, "columnSpan": 12, "rowSpan": 1, "areas": [] } ] } ] }
```

`contentData`, `settingsData` and `expose` are the same as for Block List, with every block
listed, nested ones included.

The Block Grid shapes come from an Umbraco 17.3.5 site. After your first write, read the item
back with `content get` and compare. Sites migrated from
older versions can return `contentUdi` / `settingsUdi` on layout items. Those fields are legacy;
write `contentKey` / `settingsKey`.

The `export` / `diff` / `apply` trio has its own section:
[content (export / diff / apply)](#content-export--diff--apply).

## `document-blueprint`

Content templates (blueprints) that authors start a new document from, keyed off a document
type. `get`/`scaffold` return raw JSON (full fidelity); `create` takes flags, a `--json-body`, or
`--from-document`, and `update` merges like `content update`.

```bash
umbraco document-blueprint list [--parent <folder>] [--skip <n>] [--take <n>] [--all]   # --parent lists a folder's children
umbraco document-blueprint get <id>                        # raw JSON (full fidelity) plus top-level name and parent
umbraco document-blueprint scaffold <id>                   # pre-filled create body; pipe it into content create --json-body -
umbraco document-blueprint create --document-type <alias|id> --name <name> [--culture <code>] [--parent <folder>] [--id <guid>]
umbraco document-blueprint create --json-body <file>        # the whole request; --schema prints its JSON Schema
umbraco document-blueprint update <id> [--name <name> [--culture <code>]] [--json-body <file>] [--replace]   # merges; --replace needs --yes
umbraco document-blueprint delete <id>                     # needs --yes non-interactively
umbraco document-blueprint create --from-document <id> --name <name> [--parent <folder>] [--id <guid>]   # copies a document; --name applies to every culture
umbraco document-blueprint move <id> [--parent <folder>]   # omit --parent to move to the root; --target works too

# folder sub-noun (organise blueprints in the tree):
umbraco document-blueprint folder get <id>
umbraco document-blueprint folder create --name <name> [--parent <folder>] [--id <guid>]
umbraco document-blueprint folder update <id> --name <name>
umbraco document-blueprint folder delete <id>              # needs --yes non-interactively
```

## `media`

```bash
umbraco media list [--parent <id>] [--trashed]           # --trashed: the recycle bin
umbraco media tree [--parent <id>] [--recursive] [--depth <n>]   # flat walk; each row carries depth + parentId (cap 50)
umbraco media find --name <text> | --path <a/b/c> [--parent <id>] # locate by name (server search) or by name path
umbraco media get <id>                                     # includes urls[] and file metadata in values[]; mediaType.alias is the real alias
umbraco media upload <file> [--parent <id>] [--name <name>] [--media-type <id|alias|name>] [--id <guid>] [--value alias=value]...  # staged via temporary-file
umbraco media update <id> [--name <name>] [--value alias=value]... [--json-body <file>] [--replace]   # merged like content update; the file is kept
# --id keeps the item's GUID across instances (content references media by id);
# --value sets other properties, e.g. a custom media type's required fields
umbraco media delete <id>                                  # permanent; needs --yes
umbraco media trash <id>                                   # move to recycle bin (reversible)
umbraco media restore <id> [--parent <id> | --to-root]     # restore from recycle bin; default = original parent
umbraco media empty-recycle-bin                            # permanent; needs --yes; see it first with list --trashed
umbraco media move <id> [--parent <id>]                    # --target works too
umbraco media sort [--parent <id>] (--order <id>,<id>... | --by name|createDate|updateDate [--desc])   # reorder a folder's children

# folder sub-noun (organise uploads):
umbraco media folder create --name <name> [--parent <id>] [--id <guid>]

# promotion pipeline (keeps every item's GUID; see below):
umbraco media export --out <dir> [--root <id>]             # media.json + files/<id>/<name>
umbraco media diff <dir> [--verify-files]                  # read-only
umbraco media apply <dir> [--verify-files] [--prune] [--dry-run]   # --prune trashes, needs --yes
```

A media folder is an ordinary media item of the **Folder** media type, so it moves, trashes and
deletes with the usual `media` verbs, and its id is what `media upload --parent` takes.

### `media` export / diff / apply

Moves media between environments with the same GUIDs, so content that references media by id
keeps pointing at it. Run it after `schema apply` (media types must exist) and before
`content apply`.

```bash
umbraco media export --out ./media-snapshot                # every item and file
umbraco media export --root <id> -O ./blog-images          # a subtree (root included)
umbraco media diff ./media-snapshot                        # read-only
umbraco media apply ./media-snapshot --dry-run             # preview the plan
umbraco media apply ./media-snapshot                       # create + update, uploading files
umbraco media apply ./media-snapshot --prune --yes         # also trash what the snapshot omits
```

- **A directory, not a file** - `media.json` holds each item as `{ id, parent, body, file }` in tree
  pre-order, and `files/<id>/<name>` holds the files. `--out` is required, and the snapshot cannot
  be piped (`-` is refused). Export into a new or empty directory, or over an earlier media export,
  which it replaces only once the new export is complete (a failed export leaves it as it was).
  A snapshot whose file paths leave `files/`, or pass through a symbolic link or junction, is
  refused, and apply uploads a file only when it matches the SHA-256 the index records.
- **What is compared** - the item body without what differs on every instance (the file's `src`
  folder, the server-computed size, dimensions and extension, dates, `isTrashed`, `flags`, and the
  `mediaType` icon, which is schema: the type is compared by `id`), and the
  file by name and size. `--verify-files` also downloads each live file and compares SHA-256.
  A changed file shows as `file` in `changes`.
- **Apply** - creates in pre-order with the snapshot id and parent, staging each file through
  `/temporary-file`; updates replace the body, uploading the file only when it changed. It never
  moves an item (`Drifted` rows are reported only).
- **Prune trashes** - `--prune` moves omitted items (within the snapshot's scope) to the recycle
  bin, children first; `media restore` brings one back. An item with something the snapshot keeps
  under it is left alone and shows as `skipped`.
- **Files the site will not serve** - a file that is gone (404) or that the site protects (403;
  the Management API token is not a site login) cannot be downloaded. Its item is exported
  without it, marked `fileUnavailable` in `media.json`, and listed in the export's
  `unavailableFiles`. Apply creates such an item without a file and leaves an existing one's file
  alone. Any other download failure stops the export.
- **Limits** - files are downloaded from the configured host only (never a CDN on another host,
  which would receive the token). Only `umbracoFile` is carried as a file. An item whose id is in
  the target's recycle bin cannot be created until it is restored or the bin emptied.

## `media-type`

```bash
umbraco media-type list
umbraco media-type get <id|alias|name>                   # the full Management API body: properties, groups, allowed children
umbraco media-type create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root]
umbraco media-type create --json-body <file> [--id <guid>]
umbraco media-type update <id|alias|name> --json-body <file> [--replace]   # merged into the type; --replace needs --yes
umbraco media-type create --schema | --example            # the body's JSON Schema (offline), or a real media type (needs a host)
umbraco media-type delete <id|alias|name> [--force]                # deletes every media item of the type too; refused while items (recycle bin included) or compositions use it unless --force; --yes non-interactively
```

## `document-type`

```bash
umbraco document-type list
umbraco document-type get <alias|id>                       # the full Management API body - a valid update --json-body
umbraco document-type create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root] [--description <text>] [--id <guid>]
umbraco document-type create --json-body <file> [--id <guid>]  # full Management API body: properties, groups, compositions; returns the saved type, as get prints it
umbraco document-type update <alias|id> --json-body <file> [--replace]   # merged into the type; --replace sends the whole type and needs --yes
umbraco document-type create --schema                      # the create body's JSON Schema, from the Management API spec (offline)
umbraco document-type create --example                     # a real document type (a minimal one on an empty site) to start from (needs a host)
umbraco document-type delete <id|alias> [--force]                # deletes every document of the type too; refused while documents (recycle bin included) or compositions use it, or for an element type, unless --force; --yes non-interactively
```

### Authoring a document type with properties

The flag-built `create` makes an empty type. Properties, groups, compositions, allowed templates
and culture variance are too structured for flags, so they go in a full body. Read one, edit it,
write it back:

```bash
umbraco document-type get blogPost -o json | jq .data > t.json
# ...edit t.json: add a property, a group, a template...
umbraco document-type update blogPost --json-body t.json
```

`--schema` prints the body's **JSON Schema**, offline, straight from the Management API spec.
For an update it requires no key (the body is merged). `--example` prints **a real type off the instance** instead,
which needs a host and is usually the better starting point for a body you will edit.

`get` prints the Management API body verbatim, so its output is a valid `--json-body` as it
stands. `update` merges the body's **top-level keys** into the type: a key you leave out keeps
its value, and a key you send replaces it whole (a `properties` array is the complete list, not a
delta). `--replace` sends the body as the whole type instead. The type's own id always wins over
an `id` in the body. On a site with no document types yet, `--example` prints a minimal valid body
rather than failing. The same applies to `data-type`, `media-type`, `member-type` and
`template`. All take the alias (or name) or the id.

## `data-type`

```bash
umbraco data-type list [--parent <folder>]                # includes editorAlias (one read per 40 items) and the folder as parent
umbraco data-type get <name|id>                           # by NAME (a data type has no alias); the full body, configuration included
umbraco data-type create --name <name> --editor-alias <alias> --editor-ui-alias <alias>
umbraco data-type create --json-body <file> [--id <guid>]  # full body, including the editor's `values` configuration; returns the saved data type, as get prints it
umbraco data-type update <name|id> [--name <name>] [--editor-alias <alias>] [--editor-ui-alias <alias>]
umbraco data-type update <name|id> --json-body <file> [--replace]   # merged; the only way to set `values`; --replace needs --yes
umbraco data-type create --schema | --example             # the body's JSON Schema (offline), or a real data type (needs a host)
umbraco data-type delete <id|name> [--force]                  # refused while in use unless --force (deletes the properties and their values); --yes non-interactively
umbraco data-type is-used <id|name>                       # whether any content type uses it
umbraco data-type referenced-by <id|name> [--skip <n>] [--take <n>] [--all]   # a list; each row's `kind` says what it is
umbraco data-type copy <id|name> [--parent <folder>]      # omit --parent to copy to the root; returns the copy; --target works too
umbraco data-type move <id|name> [--parent <folder>]      # omit --parent to move to the root; --target works too

# folder sub-noun (organise data types in the tree):
umbraco data-type folder get <id>
umbraco data-type folder create --name <name> [--parent <folder>] [--id <guid>]
umbraco data-type folder update <id> --name <name>
umbraco data-type folder delete <id>                      # needs --yes non-interactively
```

### Configuring the editor (`values`)

A data type's **`values`** array is its editor configuration - a dropdown's items, a numeric
range, a media picker's start node. It's different for every editor, so there are no flags for
it - pass it in a body instead:

```bash
umbraco data-type create --json-body categories.json
```

```json
{
  "name": "Blog Categories",
  "editorAlias": "Umbraco.DropDown.Flexible",
  "editorUiAlias": "Umb.PropertyEditorUi.Dropdown",
  "values": [{ "alias": "items", "value": ["News", "Opinion"] }]
}
```

Both `update` forms take the **name** or the id, as `get` does.

For the Block List and Block Grid configuration (`blocks`, `validationLimit`, areas), and the
property value that goes with it, see
[Block List and Block Grid](#block-list-and-block-grid).

## `language`

```bash
umbraco language list
umbraco language create --culture <code> [--default] [--mandatory] [--fallback <code>]
umbraco language update <id> [--name <name>] [--default] [--mandatory] [--fallback <code>]   # <id> is the ISO code; omitted fields are kept
umbraco language delete <id> --force                # --force always required (deletes the culture's variants and translations), plus --yes non-interactively
```

## `template`

```bash
umbraco template list                                     # every template, nested ones too, with its alias
umbraco template get <id|alias>                          # includes the view `content`
umbraco template create --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco template update <id|alias> [--name <name>] [--alias <alias>] [--content <razor> | --content-file <file>]   # omitted fields are kept
umbraco template update <id|alias> --json-body <file> [--replace]   # the body `template get` prints, merged; --replace needs --yes
umbraco template delete <id|alias> [--force]              # refused while a document type allows or defaults to it unless --force; --yes non-interactively
```

## `member`

```bash
umbraco member list [--group <name>]                      # filters by member group
umbraco member get <id>                                   # id only; groups as [{id, name}], memberType.alias, property values
umbraco member create --email <email> --name <name> --member-type <alias>
umbraco member update <id> [--email <email>] [--name <name>] [--approved] [--username <name>] [--group <name|id> ...] [--value alias=value ...] [--new-password <pw>] [--unlock]
umbraco member delete <id>                                # needs --yes non-interactively
```

`update` merges: only the fields you name change, and property values are matched on
alias + culture + segment, so setting one does not clear the rest. Two exceptions worth knowing:

- **`--group` replaces.** A group list is the membership, not a patch. Omit it to leave groups
  alone; pass every group the member should end up in.
- **`--new-password` is only ever sent when you pass it.** An update that does not mention it
  leaves the member's password untouched (no current password is needed - this is an admin
  reset). `--unlock` clears a lockout from failed logins.

`--value` takes `alias=value`; a value with no `=` is refused.

## `member-type`

```bash
umbraco member-type list
umbraco member-type get <id|alias>                       # the full body, properties and groups included
umbraco member-type create --name <name> --alias <alias> [--icon <alias>]
umbraco member-type create --json-body <file> [--id <guid>]
umbraco member-type update <id|alias> [--name <name>] [--alias <alias>] [--description <desc>] [--icon <alias>]
umbraco member-type update <id|alias> --json-body <file> [--replace]   # merged into the type; --replace needs --yes
umbraco member-type delete <id|alias> [--force]               # refused while it has members unless --force (deletes them); --yes non-interactively
```

## `member-group`

```bash
umbraco member-group list
umbraco member-group get <id|name>
umbraco member-group create --name <name>
umbraco member-group update <id|name> --name <name>
umbraco member-group delete <id|name> [--force]           # refused while it has members unless --force; --yes non-interactively
```

## `user`

```bash
umbraco user list                                         # the super-user is hidden from other users (see below)
umbraco user get <id|email|username>                      # groups as [{id, alias, name}], sections, languages + hasAccessToAllLanguages, start nodes, languageIsoCode, login record
umbraco user create --email <email> --name <name> --group <alias|name|id>... [--username <name>] [--password <pw>] [--id <guid>]   # no email sent; returns the user
umbraco user update <id|email|username> [--email <email>] [--name <name>] [--username <name>] [--group <alias|name|id>...] [--culture <iso>] [--new-password <pw>] [--disabled [true|false]] [--unlock]
umbraco user delete <id|email|username>...                # one or several; needs --yes non-interactively
umbraco user invite --email <email> --name <name> --group <alias|name|id>... [--username <name>] [--message <text>]   # --group repeatable, at least one; returns the invited user
```

A user is named by id, email or username (email first). Umbraco hides the **super-user** (the
installer's administrator) from every other user: it is missing from `user list` and cannot be
named or read unless you are signed in as it. That is Umbraco, not the CLI.

`user create` makes the user directly, as the backoffice's "Create user" does, so it needs no
SMTP. `--password` is set with a second call; if Umbraco rejects it (its password policy), the new
user is deleted again and the command fails, so it can be retried as it stands. Without
`--password` the user exists but cannot sign in until `user update --new-password` sets one.
`--username` defaults to the email.

`user update` merges: only the options you give change. `--group` **replaces** the user's groups
(pass every group they should end up in). `--new-password` is an admin reset and needs no current
password; `--disabled` disables the user and `--disabled false` enables them; `--unlock` clears a
lockout from failed logins. The profile, password, state and lockout are separate Umbraco calls,
made in that order; if one fails, the error says which had already been applied. Start nodes and
root access set on the user itself are kept as they are.

`user delete` may be refused by Umbraco for a user who has signed in (the error names which users have); disable them instead.

`user invite` needs SMTP configured on the site, because Umbraco emails the invitation; without it
the invite is refused and no user is created. `--username` defaults to the email.

## `user-group`

```bash
umbraco user-group list
umbraco user-group get <id|alias|name>                    # includes documentPermissions: [{document, verbs}]
umbraco user-group create --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--culture <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access | --document-start-node <id>] [--media-root-access | --media-start-node <id>] [--document-permission <id>=<verb>,<verb>]... [--id <guid>]
umbraco user-group update <id|alias|name> [--alias <alias>] [--name <name>] [--icon <alias>] [--description <text>] [--section <alias>]... [--culture <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access | --document-start-node <id>] [--media-root-access | --media-start-node <id>] [--document-permission <id>=<verb>,<verb>]...
umbraco user-group delete <id|alias|name>... [--force]   # one or several; refused while any has users unless --force; --yes non-interactively
umbraco user-group add-users <id|alias|name> --user <id|email|username>...      # --user repeatable
umbraco user-group remove-users <id|alias|name> --user <id|email|username>...   # --user repeatable
```

`update` merges: omitted options keep their values, a list option given replaces that list, and a
start node and root access clear each other.

`--document-permission` sets a granular permission on one document: the document id, `=`, and the
verbs separated by commas (`--document-permission 3f7a8b2e-...=Umb.Document.Read,Umb.Document.Update`).
It is repeatable, and on that document it takes the place of the group's fallback permissions. On
`update` it merges by document: the documents you name get the verbs you give, the group's other
document permissions are kept, and `<id>=` with no verbs removes that document's entry so the
fallback permissions apply to it again. Granular permissions of other kinds (per property value)
are not set from the CLI, and an update always keeps them. There is no media equivalent in the
Management API.

## `user-data`

Key/value data scoped to the **authenticated** user.

```bash
umbraco user-data list [--group <group>] [--identifier <id>] [--skip <n>] [--take <n>] [--all]
umbraco user-data get <id>
umbraco user-data create --group <group> --identifier <id> --data <value> [--id <guid>]
umbraco user-data update <id> [--group <group>] [--identifier <id>] [--data <value>]   # omitted fields are kept
umbraco user-data delete <id>                              # needs --yes non-interactively
```

## `dictionary`

```bash
umbraco dictionary list [--parent <key|id>]                # one level: the root, or the direct children of --parent
umbraco dictionary tree [--parent <key|id>] [--recursive] [--depth <n>]   # walk the hierarchy, like content tree
umbraco dictionary get <id|key>
umbraco dictionary create --key <key> [--value en-US=Hello --value da-DK=Hej] [--value-file en-US=<file|-> ...] [--parent <key|id>]   # --parent creates under an item
umbraco dictionary update <id|key> [--key <key>] [--value en-US=Home ...] [--value-file en-US=<file|-> ...]   # merges by ISO code
umbraco dictionary move <id|key> [--parent <key|id>]       # reparent; omit --parent to move to the root; --target works too
umbraco dictionary delete <id|key> [--force]               # refused while it has child items unless --force; --yes non-interactively
```

**Use full ISO codes in `--value`** (`en-US`, not `en`). Umbraco silently drops any code that
isn't one of the site's languages, so `create` checks them first and **fails with the list of
configured codes**. Short codes are refused rather than guessed, since a site can have both
`en-US` and `en-GB`. The item is read back afterwards, so what you see is what was stored.

**Multi-line values go in a file:** `--value-file en-US=intro.md` reads the translation from the
file as UTF-8 and stores it exactly as the file holds it, final newline included, so Markdown or
HTML needs no shell quoting. `en-US=-` reads it from stdin, and only one `--value-file` can do
that. Mix it with `--value` for other languages
(`dictionary update Blog.Intro --value-file en-US=intro.md --value da-DK=Hej`), but give each ISO
code once: the same code in both options is refused.

`get`, `create`, `update` and `list` carry the item's `parent: {id}` (left out at the root), so
you can see where an item lives without walking the tree.

`update` merges translations **by ISO code**, so naming one language leaves the others alone,
and it keeps the item's id. It applies the same ISO-code check as `create`.

**`meta.valueFormat` says what format the site stores translations in:** `text`, or `html` /
`markdown` when a package on the site declares it (see
[Declaring CLI support in your package](extensions.md)). `get` and `list` report it, and it costs
one extra request. It is information, not a rule: `create` and `update` store any value as given.
It is absent when the CLI could not read the site's package manifests, and when two packages
declare different formats it is `text` with a `warning:` on stderr.

## `webhook`

```bash
umbraco webhook list
umbraco webhook get <id|name>
umbraco webhook create --url <url> --event <name>... [--name <name>] [--description <text>] [--header <name=value>]... [--type <id|alias>]... [--id <guid>]   # --event repeatable
umbraco webhook update <id|name> [--url <url>] [--event <name>...] [--name <name>] [--description <text>] [--enabled [true|false]] [--header <name=value>]... [--type <id|alias>]... [--replace]   # --replace needs --yes non-interactively
umbraco webhook delete <id|name>                          # needs --yes non-interactively
umbraco webhook event list                                # the aliases --event accepts
umbraco webhook log list [<id|name>] [--skip <n>] [--take <n>]   # deliveries of one webhook, or of all
```

`get`, `update` and `delete` take the webhook's id or its name (ignoring case; a name shared by
two webhooks is refused with both ids). A webhook without a name can only be named by its id.

`update` merges: omitted options keep their values, `--event` and `--type` replace those lists,
and `--header` merges by header name (ignoring case), so naming one header leaves the others
alone; `--header Name=` (an empty value) removes that header. Enable or disable a webhook with `--enabled true` / `--enabled false`; there are no
separate `enable`/`disable` verbs. `update` returns the webhook read back from the instance, as
`get` shows it. New `--event` aliases get the same check as on `create`.

`update --replace` sets exactly the headers and types given instead of merging: any header or
type not named is removed, and with none given the headers are cleared and the type filter is
removed (the webhook then fires for every type). It is destructive, so it needs `--yes`
non-interactively. Events and the other fields keep their usual rules - omitted events are kept,
because a webhook with no events never fires.

`--header name=value` is repeat-only (a value may contain a comma) and is sent with every
delivery - typically an API key the receiver checks. `--type` restricts the webhook to items
of the given types: document types for content events, media types for media events, member
types for member events. It takes ids or aliases; an alias is looked up across all three kinds
and must name exactly one type; an unknown alias is refused with the nearest real one suggested.
A filter the webhook's events can never match (only document types on media events, say) is
refused, as Umbraco would never fire the webhook; a type given by id skips that check. With no
`--type` the webhook fires for every type (the JSON field is `contentTypeKeys`, as Umbraco
names it).

`log list` shows delivery attempts: `statusCode` (as Umbraco records it, e.g. `OK (200)`),
`isSuccessStatusCode`, `exceptionOccurred`, `retryCount`, and the request and response headers
and bodies. With no webhook it lists every webhook's deliveries.

`create` returns the webhook read back from the instance, in the same shape as `list`: each event
is `{eventName, eventType, alias}`, with the display name in `eventName` and what you passed to
`--event` in `alias`.

`--event` takes Umbraco's event **aliases** (`Umbraco.ContentPublish`, `Umbraco.MediaSave`),
not display names. Umbraco saves a webhook with an unknown event but never fires it, so
`create` checks every alias against `GET /webhook/events` first. An unknown alias is refused
with the nearest real one suggested, and the create is also refused when the event list can't
be read.

## `script` / `stylesheet` / `partial-view` (static files)

The three static-file resources share the same path-addressed verbs. Files are identified by
**path** (not an id); `update` replaces the content, renames the file in its folder with
`--name` (the new name with its extension), or both, and returns the file at its new path.
Umbraco has no rename for folders. Give `--content` or `--content-file` (`-` for stdin), not
both. Paths are Umbraco's form, with a leading `/` (`/blocklist/site.css`),
in every output: `create` reads the new file back, so its `path` matches `list` and `get`.
`--parent` takes the folder with or without the slashes (`blocklist`, `/blocklist/`).

```bash
# script
umbraco script list [--parent <folder>]                    # tree root, or a folder's children
umbraco script get <path>                                  # includes the file content
umbraco script create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco script update <path> [--content <text> | --content-file <file>] [--name <file>]   # --name renames it
umbraco script delete <path>                               # needs --yes non-interactively
umbraco script folder create --name <name> [--parent <folder>]   # a folder a file can go in
umbraco script folder delete <path>                        # an empty folder; needs --yes non-interactively

# stylesheet
umbraco stylesheet list [--parent <folder>]
umbraco stylesheet get <path>
umbraco stylesheet create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco stylesheet update <path> [--content <text> | --content-file <file>] [--name <file>]
umbraco stylesheet delete <path>                           # needs --yes non-interactively
umbraco stylesheet folder create --name <name> [--parent <folder>]
umbraco stylesheet folder delete <path>                    # an empty folder; needs --yes non-interactively

# partial-view
umbraco partial-view list [--parent <folder>]
umbraco partial-view get <path>
umbraco partial-view create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco partial-view update <path> [--content <text> | --content-file <file>] [--name <file>]
umbraco partial-view delete <path>                         # needs --yes non-interactively
umbraco partial-view folder create --name <name> [--parent <folder>]   # e.g. --name Components --parent blocklist
umbraco partial-view folder delete <path>                  # an empty folder; needs --yes non-interactively
```

## `tag` / `culture` (read-only)

```bash
umbraco tag list [--group <group>] [--culture <iso>]      # tags, with node counts
umbraco culture list                                      # available cultures (isoCode + name)
```

## `server` (read-only)

```bash
umbraco server status                                      # runtime status
umbraco server info                                        # version + runtime mode
umbraco server configuration                               # public config flags
umbraco server troubleshooting                             # troubleshooting items (name/value)
```

## `health`

```bash
umbraco health list [--skip <n>] [--take <n>] [--all]              # health-check groups
umbraco health get <group>                                 # a group and the checks it contains
umbraco health run <group>                                 # run the group (POST, so blocked by --readonly)
```

Each check in a `run` result carries its `id`, `name` and `description` (read from the group),
then its `results`.

`run` is a POST, so it is a write: `--readonly` blocks it, `--dry-run` previews it, and
`umbraco commands` reports it `mutating: true`. Its result is the report you ran it for, though,
so unlike other writes it still prints under `--quiet`.

## `log-viewer`

```bash
umbraco log-viewer list [--level <Verbose|Debug|Information|Warning|Error|Fatal>]... [--filter <expr>] [--start-date <date>] [--end-date <date>] [--skip <n>] [--take <n>] [--all] [--asc]
umbraco log-viewer levels [--skip <n>] [--take <n>] [--all]        # loggers and their minimum levels
umbraco log-viewer level-count [--start-date <date>] [--end-date <date>]   # message counts by level
umbraco log-viewer message-templates [--skip <n>] [--take <n>] [--all] [--start-date <date>] [--end-date <date>]

# saved-search sub-noun:
umbraco log-viewer saved-search list [--skip <n>] [--take <n>] [--all]
umbraco log-viewer saved-search create --name <name> --query <query>
umbraco log-viewer saved-search delete <name>              # needs --yes non-interactively
```

`--level` is a typed enum; repeat it for several levels. An unknown value is rejected at parse
time. Serilog filter expressions commonly start with `@` (e.g. `@Level='Error'`,
`@Exception is not null`); these are taken literally (the CLI disables `@file` response-file
expansion), so no escaping is needed:

```bash
umbraco log-viewer list --filter "@Level='Error'"
umbraco log-viewer saved-search create --name Errors --query "@Level='Error'"
```

`list --all` newest-first pins every page after the first to end at the first page's newest
entry, so entries logged during the walk do not shift the pages and repeat rows.

## `models-builder`

```bash
umbraco models-builder dashboard                           # dashboard status
umbraco models-builder status                              # whether generated models are out of date
umbraco models-builder build                               # regenerate source files (POST, --readonly-blocked); returns the status after
```

## `manifest` (read-only)

```bash
umbraco manifest list [--scope All|Public|Private]         # default: All
```

Each manifest carries `cliCapabilities` when its package declares what it changes about the CLI's
commands (the human table's CLI column shows the same). See
[Declaring CLI support in your package](extensions.md).

## `redirect`

```bash
umbraco redirect list [--content-item <id>] [--filter <s>] [--skip <n>] [--take <n>] [--all]   # --content-item lists redirects to that document
umbraco redirect delete <id>                               # needs --yes non-interactively
umbraco redirect tracking status                           # whether automatic URL-redirect tracking is enabled
umbraco redirect tracking enable                           # site-wide toggle; returns the status after
umbraco redirect tracking disable                          # site-wide toggle; needs --yes non-interactively
```

Both toggles re-read the status afterwards and fail if it did not change. On Umbraco 17 the API
accepts the request and can leave tracking as it was, because it is set by configuration
(`Umbraco:CMS:WebRouting:DisableRedirectUrlTracking` in appsettings, then restart).

## `relation-type` / `relation` (read-only)

```bash
umbraco relation-type list [--skip <n>] [--take <n>] [--all]
umbraco relation-type get <id|alias>
umbraco relation list --relation-type <id|alias> [--skip <n>] [--take <n>] [--all]   # relations are listed only by relation type
```

## `indexer` / `searcher`

```bash
umbraco indexer list [--skip <n>] [--take <n>] [--all]             # Examine indexes, with health + document counts
umbraco indexer get <name>
umbraco indexer rebuild <name>                             # expensive (POST, --readonly-blocked); not gated - nothing is lost

umbraco searcher list [--skip <n>] [--take <n>] [--all]            # registered multi-searchers; often empty (Umbraco 17)
umbraco searcher query <index|searcher> --term <term> [--skip <n>] [--take <n>] [--all]   # e.g. ExternalIndex; an index's searcherName is mapped to the index
```

## `imaging` (read-only)

```bash
umbraco imaging resize-urls <id>... [--width <px>] [--height <px>] [--mode <Crop|Max|Stretch|Pad|BoxPad|Min>] [--format <fmt>]   # one or more media ids
```

## `property-type` (read-only)

```bash
umbraco property-type is-used --document-type <id|alias> --alias <alias>
```

An `--alias` that neither the type nor its compositions has fails with `invalid_argument`,
listing the aliases it does have, rather than answering `false`.

## `schema` (export / diff / apply)

Export the site's **schema** - document types, media types, member types, data types,
templates, languages, dictionary items, member and user groups, and the partial views,
stylesheets and scripts the templates render - to a portable JSON snapshot, diff it against a
live instance, and apply the difference. Handy for moving schema between environments and for
catching drift in CI.

```bash
umbraco schema export --out schema.json                    # export every schema entity, static files included
umbraco schema export --no-files --out schema.json         # leave the static files out (views deploy from git)
umbraco schema diff schema.json                            # what differs (read-only; empty == in sync)
umbraco schema export | umbraco schema diff -              # pipe an export straight into a diff
umbraco schema apply schema.json --dry-run                 # preview the full apply plan
umbraco schema apply schema.json                           # reconcile (create + update; never deletes by default)
umbraco schema apply schema.json --prune --yes             # also delete live entities absent from the snapshot
umbraco schema apply schema.json --prune --force --yes     # ...even types in use, languages, dictionary parents, files a template names
```

How it works:

- **Hand-written snapshots** - a snapshot doesn't have to come from `export`, or carry every
  kind:
  - **A section that is absent is not managed**, for every kind, not just the files: diff and
    apply skip it and `--prune` deletes none of it. So a file with only `documentTypes` changes
    document types and nothing else. A section that is present is the whole list for that kind:
    `--prune` deletes the live ones it does not name.
  - **References may name their target** instead of giving its id, as a bare string or as
    `{ "id": "..." }`: a property's `dataType`, a type's `collection`, `compositions`,
    `allowedDocumentTypes` / `allowedMediaTypes`, `allowedTemplates` and `defaultTemplate`, a
    template's `masterTemplate`, a dictionary item's `parent`, and a user group permission's
    `documentType`. In `compositions` the reference is the item's `documentType` / `mediaType` /
    `memberType`, in `allowedDocumentTypes` / `allowedMediaTypes` its `documentType` /
    `mediaType`. Those three lists also take the bare reference as the whole item
    (`"allowedDocumentTypes": ["blogPost"]`): an allowed type gets its place in the list as its
    `sortOrder`, a composition is a `Composition`. A permission must be written out in full. A name
    is looked up in the snapshot first (so a snapshot can create a data type and use it), then on
    the instance, by alias then name, ignoring case - the same rules as every `<id>` argument. A
    name that is only a snapshot entry's display name, while the instance holds another item under
    it, is ambiguous.
  - **Ids may be left out.** An entity, property or container without an `id` takes the id of the
    live one it matches (an entity by its key, a property by alias, a container by type, name and
    parent), or a new one. Reusing the live id matters: a property sent with a new id would be a
    new property, and the old one's values would go.
  - **Containers by name.** A property's `container` and a container's `parent` may name a
    container of the same type by name, or by `Tab/Group` path when a name is used twice.
  - A name that matches nothing, or more than one thing, fails the diff or apply before anything
    is written, saying where it is (`documentType 'blogPost': properties[subtitle].dataType ...`).

  ```jsonc
  // add-subtitle.json - apply adds one property to blogPost and touches nothing else
  {
    "schemaVersion": "4",
    "documentTypes": [
      {
        "alias": "blogPost", "name": "Blog Post", "icon": "icon-document",
        "allowedAsRoot": false, "variesByCulture": false, "variesBySegment": false,
        "isElement": false, "allowedTemplates": [], "allowedDocumentTypes": [], "compositions": [],
        "containers": [{ "name": "Content", "type": "Tab", "parent": null, "sortOrder": 0 }],
        "properties": [
          { "alias": "title", "name": "Title", "container": "Content", "dataType": "Textstring",
            "sortOrder": 0, "variesByCulture": false, "variesBySegment": false,
            "validation": { "mandatory": true }, "appearance": { "labelOnTop": false } },
          { "alias": "subtitle", "name": "Subtitle", "container": "Content", "dataType": "Textstring",
            "sortOrder": 1, "variesByCulture": false, "variesBySegment": false,
            "validation": { "mandatory": false }, "appearance": { "labelOnTop": false } }
        ]
      }
    ]
  }
  ```

  A **top-level field an entry leaves out is not managed** (such as `cleanup` above): diff does
  not compare it and apply keeps the live value. A field that is present, even
  as `null`, is managed. Fields the server computes (a data type's `isDeletable` and
  `canIgnoreStartNodes`) are never compared. But a list is the **whole** list: apply writes the
  snapshot's `properties` and `containers`, so list every property the type keeps (a property
  you leave out is removed, with its values). List order does not matter: properties are matched
  by alias, containers by id, and `allowedDocumentTypes` / `compositions` entries by the type they
  name (ids ignore letter case), and order is carried by `sortOrder` and `parent`. A container
  whose id is not on
  the target (a snapshot exported from another instance) is matched to the one target container
  with the same type, name and parent, and diff and apply use the target's id; when several
  match, the type is skipped with a note naming them. Run `schema diff` first; its `changes`
  column shows exactly which fields differ.
- **Static files** - the snapshot's `partialViews`, `stylesheets` and `scripts` sections hold
  each file as `{ "path": "/blocklist/default.cshtml", "content": "..." }` and each folder as
  `{ "path": "/blocklist", "isFolder": true }`, matched by path. **A section that is absent
  means those files are not managed**: diff and apply skip them and `--prune` deletes none. That
  is what `export --no-files` writes. A section that is present but empty
  does manage them, so `--prune` deletes the live ones. Apply writes the snapshot content byte
  for byte. It creates folders shallowest first, then files, all before the templates that
  render them; a folder a file sits in is created even when the snapshot does not list it.
  `--prune` deletes files after the templates, then folders deepest first. A folder that still
  holds something the snapshot keeps is never pruned. A change that is only line endings or
  trailing newlines is `Changed` with the note `line endings only`.
- **Pruning a file a template names is refused** - before `--prune` deletes a partial view,
  stylesheet or script, it searches the templates (as they will be after the apply) for the file
  name, and for a partial view also its path without the extension in quotes
  (`Html.PartialAsync("header")` names `/header.cshtml`). A match is refused unless `--force`,
  naming the templates. A text search cannot see a name built at run time, or a partial that
  block list/grid rendering finds by convention, so check those yourself.

- **In-use prunes are refused** - before the first write, `--prune` checks every item it would
  delete. A data type still in use, a member type with members, a document or media type that
  has items (the recycle bin counted), is a composition of another type, or is an element type
  (block content can use it, and Umbraco does not say where), any language (its content variants and dictionary
  translations go with it), and a dictionary item with children the snapshot keeps under it
  (not ones this apply moves elsewhere) are refused
  unless `--force` is given, and then nothing at all is applied. `--dry-run` shows those deletes
  as `needs --force`. The default language and user groups Umbraco marks undeletable are never
  deleted: diff lists them as `Skipped` with the reason.
- **Fidelity** - the snapshot stores each entity's verbatim Management-API body, so nothing is
  lost (document-type properties/compositions, data-type configuration, template Razor). The
  snapshot is
  `{ schemaVersion, documentTypes[], mediaTypes[], memberTypes[], dataTypes[], templates[], languages[], dictionaryItems[], memberGroups[], userGroups[], partialViews[]?, stylesheets[]?, scripts[]? }`,
  at **snapshot version 4**. A version-3 snapshot has no static-file sections, so it is still
  read, with files not managed. Anything older is refused - re-export it.
- **Two kinds are shaped, not verbatim** - a dictionary item gets its `parent` (the item read has
  none) and its translations sorted by ISO code. A user group leaves out its document and media
  start nodes and its per-document permissions, since they name content on one instance; apply
  keeps the target's own. Domains are not in the snapshot: set them per environment with
  `content domain set`.
- **Matching** - diff/apply pair a snapshot entity to a live one by **id first, then human key**
  (alias for document, media and member types, templates and user groups; name for data types,
  dictionary items and member groups; ISO code for languages, which have no id), so a
  snapshot is idempotent against
  its own instance and portable to another. A key match whose ids differ is flagged
  `idMismatch`, and the id difference alone is not a change (apply cannot change an id). A
  dictionary item's parent is translated to the target's id, so a tree created by hand on each
  instance matches by key and applies under the right parents.
- **Safety** - `apply` respects the global guardrails: `--dry-run` previews and writes nothing,
  `--readonly` blocks it, and `--prune` requires confirmation / `--yes`. Writes run in dependency
  order (static files -> languages -> dictionary items -> member groups -> data types -> templates -> media types
  -> member types -> document types -> user groups, topologically sorted within each: a
  language's fallback and a dictionary item's parent come first) and stop at the first failure;
  prune deletes in reverse, dictionary children before their parents. A dictionary item whose
  parent changed is moved.

## `content` (export / diff / apply)

The same `export` -> `diff` -> `apply` workflow as `schema`, for documents: move content between
environments and spot drift.

```bash
umbraco content export --out content.json                  # whole content tree
umbraco content export --root <id> --out subtree.json      # a subtree (root included)
umbraco content diff content.json                          # read-only
umbraco content apply content.json --dry-run               # preview the whole plan
umbraco content apply content.json                         # create + update + publish state
umbraco content apply content.json --no-state              # bodies only; leave publishing alone
umbraco content apply content.json --prune --yes           # also delete what the snapshot omits
umbraco content apply content.json --prune --exclude-type contactSubmission --exclude-root <id> --yes
```

- **Full fidelity** - each document is stored as its verbatim Management-API body (all variants,
  all property values). A document's raw body does not carry its parent, so placement is recorded
  separately: `{ contentVersion, root, documents[] }` where each entry is `{ id, parent, body }`,
  in tree pre-order (parents before children).
- **Content, not instance history** - diff and apply compare a normalised body: the per-variant
  `createDate`, `updateDate`, `publishDate`, scheduled dates, `flags` and `state`, and the
  top-level `isTrashed` and `flags`, are ignored, `documentType` is compared by `id` only (its
  `icon` and `collection` are schema), and `values`/`variants` are compared in a fixed
  order. The same content on two instances is `Unchanged`, and a second `apply` does nothing.
- **Label values are not compared** - Umbraco ignores values sent for `Umbraco.Label` properties
  (values set by site code), so a promotion can never change them. The diff leaves them out, and
  `apply` warns on stderr once per Label property whose snapshot value it could not promote.
- **Rows say what they are** - every `diff` row and every `apply` / `--dry-run` row carries the
  document's `name` (the invariant or default-language variant's) and its `documentType` alias,
  so a prune plan can be reviewed before `--yes`.
- **Publish state** - apply publishes each culture the snapshot has published (`Published` or
  `PublishedPendingChanges`) and unpublishes live cultures the snapshot has not, parents first.
  A difference in publish state alone is a `Changed` row that apply publishes or unpublishes
  without an update. `--no-state` turns this off.
- **Identity** - documents are matched by **GUID only** (they have no stable natural key). Apply
  creates a document with its snapshot GUID, so the same content has the same identity in every
  environment.
- **Scope-safe prune** - the snapshot records its export `root`, and diff/apply compare against
  the same live scope, so a subtree snapshot's `--prune` can never delete documents outside the
  subtree.
- **Prune a subtree, not the whole site** - a whole-tree `--prune` also deletes everything created
  on the target since the export: form submissions, editors' drafts. Export with `--root` to
  prune one subtree, and use `--exclude-type <alias|id>` / `--exclude-root <id>` (both repeatable)
  to leave content alone. A removed document with a kept document still under it (one the
  snapshot places elsewhere) is not deleted either - apply does not move documents, and the delete
  would cascade - and shows as `skipped`. An excluded document's removed ancestors are kept too, because deleting
  a document deletes everything under it. Run `--dry-run` first.
- **Safety** - `apply` respects the global guardrails; it creates, updates and publishes/unpublishes by
  default and requires **both** `--prune` and `--yes` to delete. Creates run parent-first, then
  updates, then unpublishes (deepest-first) and publishes (parent-first), then deletes
  deepest-first, and the run stops at the first failure.
- **Out of scope** - property-value references (to media/other content by GUID) are not
  rewritten, so referenced items must already exist in the target; and apply does not move
  existing documents (a placement drift is reported by `diff` as `Drifted` but not applied).
