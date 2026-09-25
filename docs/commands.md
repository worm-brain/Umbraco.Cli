# Command reference

Every command in the `umbraco` CLI. For the JSON envelope, exit codes, guardrails, and
non-interactive rules that apply across all of them, see [agent-guide.md](agent-guide.md). For
install and auth, see [getting-started.md](getting-started.md).

> The authoritative, always-in-sync surface is `umbraco commands` (the whole tree as JSON,
> including a `destructive` flag per command). This page is the human/agent-readable narrative
> of the same thing. If the two ever disagree, `umbraco commands` is correct - please
> [open an issue](https://github.com/worm-brain/Umbraco.Cli/issues).

## Contents

- [Global options](#global-options)
- [Discovery: `commands` and `--schema`](#discovery-commands-and---schema)
- [`auth`](#auth)
- [`content`](#content)
- [`document-blueprint`](#document-blueprint)
- [`media`](#media)
- [`media-types`](#media-types)
- [`content-types`](#content-types)
- [`data-types`](#data-types)
- [`languages`](#languages)
- [`templates`](#templates)
- [`members`](#members)
- [`member-types`](#member-types)
- [`member-groups`](#member-groups)
- [`users`](#users)
- [`user-groups`](#user-groups)
- [`user-data`](#user-data)
- [`dictionary`](#dictionary)
- [`webhooks`](#webhooks)
- [`script` / `stylesheet` / `partial-view`](#script--stylesheet--partial-view-static-files)
- [`tags` / `cultures`](#tags--cultures-read-only)
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
| `--host <url>` | Umbraco instance base URL (overrides config). |
| `--token <bearer>` | Raw bearer token (overrides stored credentials). |
| `--output json\|human\|csv` | Output format. Default: `json` when piped, `human` in a terminal. `csv` is RFC-4180 and only ever explicit. |
| `--quiet`, `-q` | Suppress success-confirmation messages; data, errors, and exit codes still emitted. |
| `--verbose` | Log the HTTP method, URL, selected headers and status to stderr. Bodies are **not** logged ([#166](https://github.com/worm-brain/Umbraco.Cli/issues/166)) - use `--dry-run` to see the request body. |
| `--dry-run` | On a write command, print the request that would be sent (method, URL, body) and exit `0` without executing. No effect on reads. |
| `--yes`, `-y` | Skip the confirmation prompt on destructive commands. **Required** to run one non-interactively. |
| `--readonly` | Block all writes for this session; reads still work. Also `UMBRACO_READONLY=1`. |
| `--fields <a,b>` | Trim JSON output (or CSV columns) to these top-level fields, in order. |
| `--profile <name>`, `-p` | Named credential profile (see [`auth`](#auth)); also `UMBRACO_PROFILE`. |
| `--config <path>` | Path to the config file. |

Colour in human output is disabled when `NO_COLOR` is set (any value) or when stdout is not a
TTY.

### Paging applies to every `list`

`list` commands return at most `--take` items, **default 20**, and say so in `meta`:

```json
"meta": { "total": 37, "skip": 0, "take": 20, "hasMore": true }
```

In a terminal a truncated list also prints a hint to stderr. Page with `--skip`/`--take` until
`hasMore` is false.

`total` and `hasMore` are **omitted when the source cannot count** - an absent `hasMore` means
"unknown", not "no", so do not read a missing `total` as a complete list. There is no `--all`
yet ([#196](https://github.com/worm-brain/Umbraco.Cli/issues/196)); it is deliberately not
faked with a large `--take`, which would be the same silent cap further out.

---

## Discovery: `commands` and `--schema`

```bash
umbraco commands                 # entire command tree as JSON (local; no host/auth)
umbraco commands --output human  # same, as an indented outline
umbraco content create --schema  # JSON Schema of a command's --json-body (local; no host/auth)
```

See [agent-guide.md](agent-guide.md#1-discover-the-surface-umbraco-commands) for details.

---

## `auth`

```bash
umbraco auth login [--host <url>] [--client-id <id>] [--client-secret <secret>]
umbraco auth logout [--profile <name>]        # clears credentials; preserves the profile's allow-list
umbraco auth whoami                           # the resolved identity
umbraco auth doctor [--output json]           # diagnose host/TLS/credentials/auth/identity/version
umbraco auth profiles                         # list credential profiles; * marks the default
umbraco auth use <profile>                    # make a profile the default
```

`auth doctor` runs a sequence of checks (host resolution, connectivity/TLS, credentials,
authentication, resolved identity, instance version), reports each as `pass`/`fail`/`warn`/`skip`
with a remediation hint, and exits `1` if any check hard-fails (warnings do not fail the run).
Run it first in any new environment. See [getting-started.md](getting-started.md#5-confirm-it-works).

## `content`

```bash
umbraco content list [--parent <id>] [--skip <n>] [--take <n>]
umbraco content tree [--parent <id>] [--recursive] [--depth <n>]   # flat walk; each row carries depth + parentId (cap 50)
umbraco content find --name <text> | --path <a/b/c> [--parent <id>] # locate by name (server search) or by name path
umbraco content get <id>                                   # values, variants, template and state
umbraco content create --content-type <alias> --name <name> [--json-body <file>] [--id <guid>] [--template <alias|id>]
umbraco content update <id> [--json-body <file>] [--replace] [--template <alias|id>]   # merges by default
umbraco content delete <id>                                # permanent; needs --yes non-interactively
umbraco content publish <id> [--cultures <csv>] [--publish-at <ts>] [--unpublish-at <ts>]   # ISO 8601 to schedule; no --cultures publishes every culture the item has
umbraco content unpublish <id> [--cultures <csv>]          # takes offline; needs --yes
umbraco content versions <id> [--culture <code>]           # version history
umbraco content rollback <version-id> [--culture <code>]   # restore a version
umbraco content trash <id>                                 # move to recycle bin (reversible)
umbraco content restore <id> [--parent <id>]               # restore from recycle bin
umbraco content empty-recycle-bin                          # permanent; needs --yes
umbraco content move <id> [--parent <id>]
umbraco content sort [--parent <id>] --children <id> <id> ...   # reorder a parent's children (order given = sort order)
umbraco content copy <id> [--parent <id>] [--include-descendants] [--relate]   # returns the copy, with its new id
umbraco content publish-descendants <id> [--cultures <csv>] [--include-unpublished] [--wait]   # --wait polls to completion
umbraco content export [--root <id>] [--out <file>]        # dump subtree/site to a snapshot
umbraco content diff <snapshot>                            # diff a snapshot vs live (read-only)
umbraco content apply <snapshot> [--no-state] [--prune [--exclude-type <alias|id>]... [--exclude-root <id>]...] [--dry-run]   # reconcile bodies + publish state; --prune deletes, needs --yes

# Bulk ops over many ids (from --file or stdin), with a per-item results array:
umbraco content bulk delete [--file ids.txt]               # permanent; needs --yes
umbraco content bulk publish [--file ids.txt] [--cultures <csv>]
umbraco content bulk unpublish [--file ids.txt] [--cultures <csv>]   # takes offline; needs --yes

# domains sub-noun (Culture and Hostnames):
umbraco content domains get <id>
umbraco content domains set <id> [--default <iso>] [--domain host=iso ...] [--replace]   # merges by hostname unless --replace
```

### Domains: a multilingual site is not reachable without them

Publishing a document in several cultures does not make those cultures reachable. Umbraco needs
a **domain binding** per culture, and without one it logs "the root node was published with
multiple cultures, but no domains are configured" and serves nothing but the default
([#180](https://github.com/worm-brain/Umbraco.Cli/issues/180)):

```bash
umbraco content domains set <root-id> --default en-US \
  --domain example.com=en-US --domain example.com/da=da-DK
```

The API replaces the whole set, so `--domain` **merges** into what is already bound, matched on
hostname. Pass `--replace` to set exactly what you name and drop the rest. Each binding must be
`host=isoCode`; a value with no `=` is refused at parse time rather than silently dropped.

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

**Merging ships in 0.1.0-alpha.11.** On **0.1.0-alpha.10 and earlier** `update` is the other
way round: it replaces values wholesale *and* silently clears the item's template, which 404s the
page once republished
([#178](https://github.com/worm-brain/Umbraco.Cli/issues/178),
[#179](https://github.com/worm-brain/Umbraco.Cli/issues/179)). On those versions, read the whole
document from the Management API, send the complete body back, and re-set the template with a
direct `PUT /umbraco/management/api/v1/document/{id}` afterwards.

### Known sharp edges on `content` (Umbraco 17.x)

Found in a hands-on test round against 17.7.0 and still open. Tracked in
[#187](https://github.com/worm-brain/Umbraco.Cli/issues/187).

| What | Effect | Do this instead |
|---|---|---|
| `content get` does not return the item's **parent** ([#168](https://github.com/worm-brain/Umbraco.Cli/issues/168)) | Placement is not on the Management API's by-id body at all - it lives in the tree | `umbraco content tree`, whose rows carry `parentId` |

A trap worth knowing when fixing templates in bulk: a change that touches **only** the template
does not mark culture variants as having pending changes, so `publish-descendants` skips them as
already published. Publish the affected cultures explicitly.

Bulk commands read ids one per line from `--file` or stdin, so you can pipe:

```bash
umbraco content list --fields id | jq -r '.data[].id' | umbraco content bulk publish
```

On Windows PowerShell, use the built-in `ConvertFrom-Json` instead of `jq` (which is not
installed by default):

```powershell
(umbraco content list --fields id --output json | ConvertFrom-Json).data.id | umbraco content bulk publish
```

Each id is reported independently in the `data` results array (`{id, status, error}`); the exit
code is `1` if any item failed. A bulk `delete` is gated by a single confirmation (`--yes`
non-interactively) - it never prompts per item.

All create commands accept `--id <guid>` for **idempotent creates** (Umbraco 14+ honours a
client-supplied id), so re-running a provisioning script does not create duplicates.

### Property value formats for `--json-body`

`content create --schema` types `values[].value` as "any", because the shape depends on the
property editor behind each property, not on the CLI. Look up a property's editor with
`umbraco schema export` (`.documentTypes[].properties[].dataType` -> `.dataTypes[].editorAlias`),
then use the matching shape below. Verified against Umbraco 17.7.0.

| Editor (data type name) | `value` shape |
|---|---|
| Textstring, Textarea (`Umbraco.TextBox`) | `"some text"` |
| Rich text (Tiptap) | `{"markup":"<p>...</p>","blocks":null}` |
| Image media picker (`Umbraco.MediaPicker3`) | `[{"key":"<new guid>","mediaKey":"<media id>","mediaTypeAlias":"Image","crops":[],"focalPoint":null}]` |
| Date picker (`Umbraco.DateTime`) | `"2026-05-01 00:00:00"` |
| Dropdown (flexible, multiple) | `["News","Opinion"]` |
| Tags | `["umbraco","cli"]` |
| Numeric | `5` |
| True/false | `true` |
| Content picker | `"<document guid>"` |

`key` on a media picker entry is the **picker entry's own** new GUID, not the media item's -
`mediaKey` carries the media id. Generate a fresh one per entry.

Where the backend `editorAlias` is not listed above it was not captured during testing - read it
off the live site with `umbraco schema export` rather than guessing.

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

The `export` / `diff` / `apply` trio has its own section:
[content (export / diff / apply)](#content-export--diff--apply).

## `document-blueprint`

Content templates (blueprints) that authors start a new document from, keyed off a document
type. `get`/`scaffold` return raw JSON (full fidelity); `create`/`update` mirror `content
create` (scalar flags OR `--json-body` OR `--schema`).

```bash
umbraco document-blueprint list [--parent <folder>] [--skip <n>] [--take <n>]   # --parent lists a folder's children
umbraco document-blueprint get <id>                        # raw JSON (full fidelity)
umbraco document-blueprint scaffold <id>                   # pre-filled create template Umbraco would use
umbraco document-blueprint create --document-type <alias|uuid> --name <name> [--parent <folder>] [--json-body <file>] [--schema] [--id <guid>]
umbraco document-blueprint update <id> [--name <name>] [--json-body <file>] [--schema]
umbraco document-blueprint delete <id>                     # needs --yes non-interactively
umbraco document-blueprint from-document <documentId> --name <name> [--parent <folder>] [--id <guid>]
umbraco document-blueprint move <id> [--target <folder>]   # omit --target to move to the root

# folder sub-noun (organise blueprints in the tree):
umbraco document-blueprint folder get <id>
umbraco document-blueprint folder create --name <name> [--parent <folder>] [--id <guid>]
umbraco document-blueprint folder update <id> --name <name>
umbraco document-blueprint folder delete <id>              # needs --yes non-interactively
```

## `media`

```bash
umbraco media list [--parent <id>]
umbraco media tree [--parent <id>] [--recursive] [--depth <n>]   # flat walk; each row carries depth + parentId (cap 50)
umbraco media find --name <text> | --path <a/b/c> [--parent <id>] # locate by name (server search) or by name path
umbraco media get <id>                                     # includes urls[] and file metadata in values[]; mediaType.alias is the real alias
umbraco media upload <file> [--parent <id>] [--name <name>] [--media-type <name|id>] [--id <guid>] [--value alias=value]...  # staged via temporary-file
# --id keeps the item's GUID across instances (content references media by id);
# --value sets other properties, e.g. a custom media type's required fields
umbraco media delete <id>                                  # permanent; needs --yes
umbraco media trash <id>                                   # move to recycle bin (reversible)
umbraco media restore <id> [--parent <id>]
umbraco media empty-recycle-bin                            # permanent; needs --yes
umbraco media move <id> [--parent <id>]
umbraco media sort [--parent <id>] --children <id> <id> ...   # reorder a parent folder's children

# folder sub-noun (organise uploads):
umbraco media folder create --name <name> [--parent <id>] [--id <guid>]
```

A media folder is an ordinary media item of the **Folder** media type, so it moves, trashes and
deletes with the usual `media` verbs, and its id is what `media upload --parent` takes
([#171](https://github.com/worm-brain/Umbraco.Cli/issues/171)).

## `media-types`

```bash
umbraco media-types list
umbraco media-types get <id|alias|name>
umbraco media-types create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root]
umbraco media-types delete <id|alias|name> --force                  # deletes every media item of the type too; --force always required, plus --yes non-interactively
```

## `content-types`

```bash
umbraco content-types list
umbraco content-types get <alias|id>                       # includes properties, groups, templates
umbraco content-types create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root] [--description <text>] [--id <guid>]
umbraco content-types create --json-body <file> [--id <guid>]  # full Management API body: properties, groups, compositions; returns {id, name, alias}
umbraco content-types update <alias|id> --json-body <file> # full replace
umbraco content-types create --schema                      # print a real document type as a worked example (needs a host)
umbraco content-types delete <id|alias> --force                  # deletes every document of the type too; --force always required, plus --yes non-interactively
```

### Authoring a document type with properties

The flag-built `create` makes an empty type. Properties, groups, compositions, allowed templates
and culture variance are too structured for flags, so they go in a full body
([#161](https://github.com/worm-brain/Umbraco.Cli/issues/161)). Read one, edit it, write it back:

```bash
umbraco content-types get blogPost -o json | jq .data > t.json
# ...edit t.json: add a property, a group, a template...
umbraco content-types update blogPost --json-body t.json
```

`--schema` on these verbs prints **a real type off the instance** rather than a hand-written
JSON Schema, so the example cannot drift from what the API actually accepts. That is why it
needs a host, unlike `--schema` on `content create`.

`update` is a full replace, so send the whole body - not a patch. Both verbs take the alias or
the id.

## `data-types`

```bash
umbraco data-types list                                    # includes editorAlias (one read per item)
umbraco data-types get <name|id>                           # by NAME (a data type has no alias); includes its configuration
umbraco data-types create --name <name> --editor-alias <alias> --editor-ui-alias <alias>
umbraco data-types create --json-body <file> [--id <guid>]  # full body, including the editor's `values` configuration; returns {id, name}
umbraco data-types update <name|id> [--name <name>] [--editor-alias <alias>] [--editor-ui-alias <alias>]
umbraco data-types update <name|id> --json-body <file>     # full replace, the only way to set `values`
umbraco data-types create --schema                         # print a real data type as a worked example (needs a host)
umbraco data-types delete <id|name> [--force]                  # refused while in use unless --force (deletes the properties and their values); --yes non-interactively
umbraco data-types is-used <id>                            # whether any content type uses it
umbraco data-types referenced-by <id> [--skip <n>] [--take <n>]   # raw JSON; mixed reference kinds
umbraco data-types copy <id> [--target <folder>]           # omit --target to copy to the root; returns the copy
umbraco data-types move <id> [--target <folder>]           # omit --target to move to the root

# folder sub-noun (organise data types in the tree):
umbraco data-types folder get <id>
umbraco data-types folder create --name <name> [--parent <folder>] [--id <guid>]
umbraco data-types folder update <id> --name <name>
umbraco data-types folder delete <id>                      # needs --yes non-interactively
```

### Configuring the editor (`values`)

A data type's **`values`** array is its editor configuration - a dropdown's items, a numeric
range, a media picker's start node. The flag-built verbs deliberately never exposed it (it is
editor-specific, so there is no fixed set of flags for it), which meant configuring one needed a
`schema export | jq | schema apply` round-trip through the whole instance
([#169](https://github.com/worm-brain/Umbraco.Cli/issues/169)). Pass the body instead:

```bash
umbraco data-types create --json-body categories.json
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

## `languages`

```bash
umbraco languages list
umbraco languages create --culture <code> [--default] [--mandatory] [--fallback <code>]
umbraco languages update <iso-code> --name <name> [--default] [--mandatory] [--fallback <code>]
umbraco languages delete <iso-code>                        # needs --yes non-interactively
```

## `templates`

```bash
umbraco templates list                                     # every template, nested ones too, with its alias
umbraco templates get <id|alias>
umbraco templates create --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco templates update <id|alias> --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco templates delete <id|alias>                        # needs --yes non-interactively
```

## `members`

```bash
umbraco members list [--group <name>]                      # filters by member group
umbraco members get <id>                                   # UUID only; includes groups and property values
umbraco members create --email <email> --name <name> --type <alias>
umbraco members update <id> [--email <email>] [--name <name>] [--approved] [--username <name>] [--group <name|id> ...] [--value alias=value ...] [--new-password <pw>] [--unlock]
umbraco members delete <id>                                # needs --yes non-interactively
```

`update` merges: only the fields you name change, and property values are matched on
alias + culture + segment, so setting one does not clear the rest. Two exceptions worth knowing:

- **`--group` replaces.** A group list is the membership, not a patch. Omit it to leave groups
  alone; pass every group the member should end up in.
- **`--new-password` is only ever sent when you pass it.** An update that does not mention it
  leaves the member's password untouched (no current password is needed - this is an admin
  reset). `--unlock` clears a lockout from failed logins.

`--value` takes `alias=value`; a value with no `=` is refused at parse time rather than silently
dropped.

## `member-types`

```bash
umbraco member-types list
umbraco member-types get <id|alias>
umbraco member-types create --name <name> --alias <alias> [--icon <alias>]
umbraco member-types update <id|alias> [--name <name>] [--alias <alias>] [--description <desc>] [--icon <alias>]
umbraco member-types delete <id|alias> [--force]               # refused while it has members unless --force (deletes them); --yes non-interactively
```

## `member-groups`

```bash
umbraco member-groups list
umbraco member-groups get <id|name>
umbraco member-groups create --name <name>
umbraco member-groups update <id|name> --name <name>
umbraco member-groups delete <id|name>                     # needs --yes non-interactively
```

## `users`

```bash
umbraco users list
umbraco users get <id|email>
umbraco users invite --email <email> --name <name> --group <alias|name|id>... [--username <name>] [--message <text>]   # --group repeatable, at least one
```

`users invite` needs SMTP configured on the site, because Umbraco emails the invitation; without it
the invite is refused and no user is created. `--username` defaults to the email.

## `user-groups`

```bash
umbraco user-groups list
umbraco user-groups get <id|alias|name>
umbraco user-groups create --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--language <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access | --document-start-node <id>] [--media-root-access | --media-start-node <id>] [--id <guid>]
umbraco user-groups update <id|alias|name> --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--language <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access | --document-start-node <id>] [--media-root-access | --media-start-node <id>]
umbraco user-groups delete <id|alias|name>                # needs --yes non-interactively
umbraco user-groups delete-many --ids <id|alias|name>...  # bulk; needs --yes non-interactively
umbraco user-groups add-users <id|alias|name> --user <id>...          # --user repeatable
umbraco user-groups remove-users <id|alias|name> --user <id>...       # --user repeatable
```

Granular per-node permissions are a deferred follow-up: `create`/`update` set the scalar and
list fields but send an empty permissions set. `update` replaces the whole group, so pass the
full desired state.

## `user-data`

Key/value data scoped to the **authenticated** user.

```bash
umbraco user-data list [--group <group>] [--identifier <id>] [--skip <n>] [--take <n>]
umbraco user-data get <key>
umbraco user-data create --group <group> --identifier <id> --value <value> [--key <guid>]
umbraco user-data update --key <key> --group <group> --identifier <id> --value <value>
umbraco user-data delete <key>                             # needs --yes non-interactively
```

## `dictionary`

```bash
umbraco dictionary list
umbraco dictionary tree [--parent <key|id>]                # browse the hierarchy: root, or children of --parent
umbraco dictionary get <key|id>
umbraco dictionary create --key <key> [--values en-US=Hello --values da-DK=Hej] [--parent <key|id>]   # --parent creates under an item
umbraco dictionary update <key|id> [--key <key>] [--values en-US=Home ...]   # merges by ISO code
umbraco dictionary move <key|id> [--target <key|id>]       # reparent; omit --target to move to the root
umbraco dictionary delete <key|id>                         # needs --yes non-interactively
```

**Use full ISO codes in `--values`** (`en-US`, not `en`). Umbraco matches them against the
site's configured languages and silently discards any it does not recognise, so `create` now
checks them first and **fails with the list of configured codes** rather than reporting a saved
item that is actually empty ([#181](https://github.com/worm-brain/Umbraco.Cli/issues/181)). The
response is also read back from the instance, so what you see is what was stored.

Short codes are rejected rather than resolved: on a site with both `en-US` and `en-GB`, guessing
which one `en` meant would be a coin flip.

`update` merges translations **by ISO code**, so naming one language leaves the others alone,
and it keeps the item's id - correcting a translation no longer means delete-and-recreate
([#182](https://github.com/worm-brain/Umbraco.Cli/issues/182)). It applies the same ISO-code
check as `create`.

## `webhooks`

```bash
umbraco webhooks list
umbraco webhooks create --url <url> --events <csv> [--name <name>] [--description <text>]
umbraco webhooks delete <id>                               # needs --yes non-interactively
```

## `script` / `stylesheet` / `partial-view` (static files)

The three static-file resources share the same path-addressed verbs. Files are identified by
**path** (not a GUID); `update` replaces the content only. The three nouns are spelled out
below so each is complete on its own.

```bash
# script
umbraco script list [--parent <folder>]                    # tree root, or a folder's children
umbraco script get <path>                                  # includes the file content
umbraco script create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco script update <path> [--content <text> | --content-file <file>]
umbraco script delete <path>                               # needs --yes non-interactively

# stylesheet
umbraco stylesheet list [--parent <folder>]
umbraco stylesheet get <path>
umbraco stylesheet create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco stylesheet update <path> [--content <text> | --content-file <file>]
umbraco stylesheet delete <path>                           # needs --yes non-interactively

# partial-view
umbraco partial-view list [--parent <folder>]
umbraco partial-view get <path>
umbraco partial-view create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco partial-view update <path> [--content <text> | --content-file <file>]
umbraco partial-view delete <path>                         # needs --yes non-interactively
```

## `tags` / `cultures` (read-only)

```bash
umbraco tags list [--group <group>] [--culture <iso>]      # tags, with node counts
umbraco cultures list                                      # available cultures (isoCode + name)
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
umbraco health list [--skip <n>] [--take <n>]              # health-check groups
umbraco health get <group>                                 # a group and the checks it contains
umbraco health run <group>                                 # run the group (POST, so blocked by --readonly)
```

## `log-viewer`

```bash
umbraco log-viewer log [--level <Verbose|Debug|Information|Warning|Error|Fatal>]... [--filter <expr>] [--start-date <date>] [--end-date <date>] [--skip <n>] [--take <n>] [--ascending]
umbraco log-viewer levels [--skip <n>] [--take <n>]        # loggers and their minimum levels
umbraco log-viewer level-count [--start-date <date>] [--end-date <date>]   # message counts by level
umbraco log-viewer message-templates [--skip <n>] [--take <n>] [--start-date <date>] [--end-date <date>]

# saved-search sub-noun:
umbraco log-viewer saved-search list [--skip <n>] [--take <n>]
umbraco log-viewer saved-search create --name <name> --query <query>
umbraco log-viewer saved-search delete <name>              # needs --yes non-interactively
```

`--level` is a typed enum; repeat it for several levels. An unknown value is rejected at parse
time. Serilog filter expressions commonly start with `@` (e.g. `@Level='Error'`,
`@Exception is not null`); these are taken literally (the CLI disables `@file` response-file
expansion), so no escaping is needed:

```bash
umbraco log-viewer log --filter "@Level='Error'"
umbraco log-viewer saved-search create --name Errors --query "@Level='Error'"
```

## `models-builder`

```bash
umbraco models-builder dashboard                           # dashboard status
umbraco models-builder status                              # whether generated models are out of date
umbraco models-builder build                               # regenerate source files (POST, --readonly-blocked); needs --yes
```

## `manifest` (read-only)

```bash
umbraco manifest list [--scope All|Public|Private]         # default: All
```

## `redirect`

```bash
umbraco redirect list [--content <key>] [--filter <s>] [--skip <n>] [--take <n>]   # --content lists redirects to that document
umbraco redirect status                                    # whether automatic URL-redirect tracking is enabled
umbraco redirect delete <id>                               # needs --yes non-interactively
umbraco redirect tracking enable                           # site-wide toggle
umbraco redirect tracking disable                          # site-wide toggle; needs --yes non-interactively
```

Both toggles re-read the status afterwards and fail if it did not change. On Umbraco 17 the API
accepts the request and can leave tracking as it was, because it is set by configuration
(`Umbraco:CMS:WebRouting:DisableRedirectUrlTracking` in appsettings, then restart).

## `relation-type` / `relation` (read-only)

```bash
umbraco relation-type list [--skip <n>] [--take <n>]
umbraco relation-type get <id>
umbraco relation list --type <relationTypeId> [--skip <n>] [--take <n>]   # relations are listed only by relation-type id
```

## `indexer` / `searcher`

```bash
umbraco indexer list [--skip <n>] [--take <n>]             # Examine indexes, with health + document counts
umbraco indexer get <name>
umbraco indexer rebuild <name>                             # expensive (POST, --readonly-blocked); needs --yes

umbraco searcher list [--skip <n>] [--take <n>]
umbraco searcher query <name> --term <term> [--skip <n>] [--take <n>]
```

## `imaging` (read-only)

```bash
umbraco imaging resize-urls --id <guid>... [--width <px>] [--height <px>] [--mode <Crop|Max|Stretch|Pad|BoxPad|Min>] [--format <fmt>]   # --id repeatable
```

## `property-type` (read-only)

```bash
umbraco property-type is-used --content-type <id> --alias <alias>
```

## `schema` (export / diff / apply)

Dump the site's **schema** - document types, media types, member types, data types and
templates - to a portable JSON snapshot, diff it against a live instance, and apply the difference. Complements uSync for CI
pipelines. (Issue #68; [ADR 0005](adr/0005-schema-export-diff-apply.md).)

```bash
umbraco schema export --out schema.json                    # export every schema entity
umbraco schema diff schema.json                            # what differs (read-only; empty == in sync)
umbraco schema export | umbraco schema diff -              # pipe an export straight into a diff
umbraco schema apply schema.json --dry-run                 # preview the full apply plan
umbraco schema apply schema.json                           # reconcile (create + update; never deletes by default)
umbraco schema apply schema.json --prune --yes             # also delete live entities absent from the snapshot
umbraco schema apply schema.json --prune --force --yes     # ...even types still in use (their content goes with them)
```

How it works:

- **In-use prunes are refused** - before the first write, `--prune` checks every type it would
  delete. A data type still in use, a member type with members, and any document or media type
  (Umbraco cannot say how many items use one) are refused unless `--force` is given, and then
  nothing at all is applied. `--dry-run` shows those deletes as `needs --force`.
- **Fidelity** - the snapshot stores each entity's verbatim Management-API body, so nothing is
  lost (document-type properties/compositions, data-type configuration, template Razor). The
  snapshot is
  `{ schemaVersion, documentTypes[], mediaTypes[], memberTypes[], dataTypes[], templates[] }`.
  Media types and member types joined in **snapshot version 2**
  ([#186](https://github.com/worm-brain/Umbraco.Cli/issues/186)); a version-1 file is refused rather than read as "this instance
  should have no media or member types", which `apply --prune` would act on. Re-export.
- **Matching** - diff/apply pair a snapshot entity to a live one by **id first, then human key**
  (alias for document, media and member types and templates, name for data types), so a
  snapshot is idempotent against
  its own instance and portable to another. An id-only-vs-key match is flagged `idMismatch`.
- **Safety** - `apply` respects the global guardrails: `--dry-run` previews and writes nothing,
  `--readonly` blocks it, and `--prune` requires confirmation / `--yes`. Writes run in dependency
  order (data types -> templates -> media types -> member types -> document types,
  topologically sorted within each) and stop at the first failure; prune deletes in reverse.

## `content` (export / diff / apply)

The content pipeline mirrors the schema one for documents (issue #100,
[ADR 0006](adr/0006-content-export-diff-apply.md)), so the same `export` -> `diff` -> `apply`
workflow moves content between environments and detects drift.

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
  top-level `isTrashed` and `flags`, are ignored, and `values`/`variants` are compared in a fixed
  order. The same content on two instances is `Unchanged`, and a second `apply` does nothing.
- **Publish state** - apply publishes each culture the snapshot has published (`Published` or
  `PublishedPendingChanges`) and unpublishes live cultures the snapshot has not, parents first.
  A difference in publish state alone is a `Changed` row that apply publishes or unpublishes
  without an update. `--no-state` turns this off.
- **Identity** - documents are matched by **GUID only** (they have no stable natural key). Apply
  recreates a document with its snapshot GUID (Umbraco 14+ honours a client-supplied id), so the
  same content has the same identity in every environment.
- **Scope-safe prune** - the snapshot records its export `root`, and diff/apply compare against
  the same live scope, so a subtree snapshot's `--prune` can never delete documents outside the
  subtree.
- **Prune a subtree, not the whole site** - a whole-tree `--prune` also deletes everything created
  on the target since the export: form submissions, editors' drafts. Export with `--root` to
  prune one subtree, and use `--exclude-type <alias|id>` / `--exclude-root <id>` (both repeatable)
  to leave content alone. An excluded document's removed ancestors are kept too, because deleting
  a document deletes everything under it. Run `--dry-run` first.
- **Safety** - `apply` respects the global guardrails; it creates, updates and publishes/unpublishes by
  default and requires **both** `--prune` and `--yes` to delete. Creates run parent-first, then
  updates, then unpublishes (deepest-first) and publishes (parent-first), then deletes
  deepest-first, and the run stops at the first failure.
- **Out of scope** - property-value references (to media/other content by GUID) are not
  rewritten, so referenced items must already exist in the target; and apply does not move
  existing documents (a placement drift is reported by `diff` as `Drifted` but not applied).
