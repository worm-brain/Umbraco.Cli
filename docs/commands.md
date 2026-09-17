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
| `--verbose` | Log HTTP requests/responses to stderr. |
| `--dry-run` | On a write command, print the request that would be sent (method, URL, body) and exit `0` without executing. No effect on reads. |
| `--yes`, `-y` | Skip the confirmation prompt on destructive commands. **Required** to run one non-interactively. |
| `--readonly` | Block all writes for this session; reads still work. Also `UMBRACO_READONLY=1`. |
| `--fields <a,b>` | Trim JSON output (or CSV columns) to these top-level fields, in order. |
| `--profile <name>`, `-p` | Named credential profile (see [`auth`](#auth)); also `UMBRACO_PROFILE`. |
| `--config <path>` | Path to the config file. |

Colour in human output is disabled when `NO_COLOR` is set (any value) or when stdout is not a
TTY.

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
umbraco content get <id>
umbraco content create --content-type <alias> --name <name> [--json-body <file>] [--id <guid>]
umbraco content update <id> [--json-body <file>]
umbraco content delete <id>                                # permanent; needs --yes non-interactively
umbraco content publish <id> [--cultures <csv>]
umbraco content unpublish <id> [--cultures <csv>]          # takes offline; needs --yes
umbraco content versions <id> [--culture <code>]           # version history
umbraco content rollback <version-id> [--culture <code>]   # restore a version
umbraco content trash <id>                                 # move to recycle bin (reversible)
umbraco content restore <id> [--parent <id>]               # restore from recycle bin
umbraco content empty-recycle-bin                          # permanent; needs --yes
umbraco content move <id> [--parent <id>]
umbraco content copy <id> [--parent <id>] [--include-descendants] [--relate]
umbraco content publish-descendants <id> [--cultures <csv>] [--include-unpublished]
umbraco content export [--root <id>] [--out <file>]        # dump subtree/site to a snapshot
umbraco content diff <snapshot>                            # diff a snapshot vs live (read-only)
umbraco content apply <snapshot> [--prune] [--dry-run]     # reconcile; --prune deletes, needs --yes

# Bulk ops over many ids (from --file or stdin), with a per-item results array:
umbraco content bulk delete [--file ids.txt]               # permanent; needs --yes
umbraco content bulk publish [--file ids.txt] [--cultures <csv>]
umbraco content bulk unpublish [--file ids.txt] [--cultures <csv>]   # takes offline; needs --yes
```

Bulk commands read ids one per line from `--file` or stdin, so you can pipe:

```bash
umbraco content list --fields id | jq -r '.[].id' | umbraco content bulk publish
```

Each id is reported independently in the `data` results array (`{id, status, error}`); the exit
code is `1` if any item failed. A bulk `delete` is gated by a single confirmation (`--yes`
non-interactively) - it never prompts per item.

All create commands accept `--id <guid>` for **idempotent creates** (Umbraco 14+ honours a
client-supplied id), so re-running a provisioning script does not create duplicates.

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
umbraco media get <id>
umbraco media upload <file> [--parent <id>] [--name <name>] [--media-type <name|id>]  # staged via temporary-file
umbraco media delete <id>                                  # permanent; needs --yes
umbraco media trash <id>                                   # move to recycle bin (reversible)
umbraco media restore <id> [--parent <id>]
umbraco media empty-recycle-bin                            # permanent; needs --yes
umbraco media move <id> [--parent <id>]
```

## `media-types`

```bash
umbraco media-types list
umbraco media-types get <id>
umbraco media-types create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root]
umbraco media-types delete <id>                            # needs --yes non-interactively
```

## `content-types`

```bash
umbraco content-types list
umbraco content-types get <id|alias>
umbraco content-types create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root] [--description <text>] [--id <guid>]
umbraco content-types delete <id>                          # needs --yes non-interactively
```

## `data-types`

```bash
umbraco data-types list
umbraco data-types get <id|alias>
umbraco data-types create --name <name> --editor-alias <alias> --editor-ui-alias <alias>
umbraco data-types update <id> --name <name> --editor-alias <alias> --editor-ui-alias <alias>
umbraco data-types delete <id>                             # needs --yes non-interactively
umbraco data-types is-used <id>                            # whether any content type uses it
umbraco data-types referenced-by <id> [--skip <n>] [--take <n>]   # raw JSON; mixed reference kinds
umbraco data-types copy <id> [--target <folder>]           # omit --target to copy to the root; needs --yes
umbraco data-types move <id> [--target <folder>]           # omit --target to move to the root; needs --yes

# folder sub-noun (organise data types in the tree):
umbraco data-types folder get <id>
umbraco data-types folder create --name <name> [--parent <folder>] [--id <guid>]
umbraco data-types folder update <id> --name <name>
umbraco data-types folder delete <id>                      # needs --yes non-interactively
```

## `languages`

```bash
umbraco languages list
umbraco languages create --culture <code> [--default]
umbraco languages update <iso-code> --name <name> [--default] [--mandatory] [--fallback <code>]
umbraco languages delete <iso-code>                        # needs --yes non-interactively
```

## `templates`

```bash
umbraco templates list
umbraco templates get <alias>
umbraco templates create --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco templates update <id> --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco templates delete <id>                              # needs --yes non-interactively
```

## `members`

```bash
umbraco members list [--group <name>]
umbraco members get <id|email>
umbraco members create --email <email> --name <name> --type <alias>
umbraco members update <id> [--email <email>] [--name <name>] [--approved]
umbraco members delete <id>                                # needs --yes non-interactively
```

## `member-types`

```bash
umbraco member-types list
umbraco member-types get <id>
umbraco member-types create --name <name> --alias <alias> [--icon <alias>]
umbraco member-types update <id> [--name <name>] [--alias <alias>] [--description <desc>] [--icon <alias>]
umbraco member-types delete <id>                           # needs --yes non-interactively
```

## `member-groups`

```bash
umbraco member-groups list
umbraco member-groups get <id>
umbraco member-groups create --name <name>
umbraco member-groups update <id> --name <name>
umbraco member-groups delete <id>                          # needs --yes non-interactively
```

## `users`

```bash
umbraco users list
umbraco users get <id|email>
umbraco users invite --email <email> --name <name>
```

## `user-groups`

```bash
umbraco user-groups list
umbraco user-groups get <id>
umbraco user-groups create --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--language <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access] [--media-root-access] [--id <guid>]
umbraco user-groups update <id> --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--language <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access] [--media-root-access]
umbraco user-groups delete <id>                            # needs --yes non-interactively
umbraco user-groups delete-many --ids <id>...              # bulk; needs --yes non-interactively
umbraco user-groups add-users <id> --user <id>...          # --user repeatable
umbraco user-groups remove-users <id> --user <id>...       # --user repeatable
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
umbraco dictionary get <key>
umbraco dictionary create --key <key> [--values en=Hello --values da=Hej]
umbraco dictionary delete <id>                             # needs --yes non-interactively
```

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
umbraco redirect tracking enable                           # site-wide toggle; needs --yes non-interactively
umbraco redirect tracking disable                          # site-wide toggle; needs --yes non-interactively
```

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

Dump the site's **schema** - document types, data types, and templates - to a portable JSON
snapshot, diff it against a live instance, and apply the difference. Complements uSync for CI
pipelines. (Issue #68; [ADR 0005](adr/0005-schema-export-diff-apply.md).)

```bash
umbraco schema export --out schema.json                    # export all doc types, data types, templates
umbraco schema diff schema.json                            # what differs (read-only; empty == in sync)
umbraco schema export | umbraco schema diff -              # pipe an export straight into a diff
umbraco schema apply schema.json --dry-run                 # preview the full apply plan
umbraco schema apply schema.json                           # reconcile (create + update; never deletes by default)
umbraco schema apply schema.json --prune --yes             # also delete live entities absent from the snapshot
```

How it works:

- **Fidelity** - the snapshot stores each entity's verbatim Management-API body, so nothing is
  lost (document-type properties/compositions, data-type configuration, template Razor). The
  snapshot is `{ schemaVersion, documentTypes[], dataTypes[], templates[] }`.
- **Matching** - diff/apply pair a snapshot entity to a live one by **id first, then human key**
  (alias for document types/templates, name for data types), so a snapshot is idempotent against
  its own instance and portable to another. An id-only-vs-key match is flagged `idMismatch`.
- **Safety** - `apply` respects the global guardrails: `--dry-run` previews and writes nothing,
  `--readonly` blocks it, and `--prune` requires confirmation / `--yes`. Writes run in dependency
  order (data types -> templates -> document types, topologically sorted within each) and stop at
  the first failure.

## `content` (export / diff / apply)

The content pipeline mirrors the schema one for documents (issue #100,
[ADR 0006](adr/0006-content-export-diff-apply.md)), so the same `export` -> `diff` -> `apply`
workflow moves content between environments and detects drift.

```bash
umbraco content export --out content.json                  # whole content tree
umbraco content export --root <id> --out subtree.json      # a subtree (root included)
umbraco content diff content.json                          # read-only
umbraco content apply content.json --dry-run               # preview the whole plan
umbraco content apply content.json                         # create + update
umbraco content apply content.json --prune --yes           # also delete what the snapshot omits
```

- **Full fidelity** - each document is stored as its verbatim Management-API body (all variants,
  all property values). A document's raw body does not carry its parent, so placement is recorded
  separately: `{ contentVersion, root, documents[] }` where each entry is `{ id, parent, body }`,
  in tree pre-order (parents before children).
- **Identity** - documents are matched by **GUID only** (they have no stable natural key). Apply
  recreates a document with its snapshot GUID (Umbraco 14+ honours a client-supplied id), so the
  same content has the same identity in every environment.
- **Scope-safe prune** - the snapshot records its export `root`, and diff/apply compare against
  the same live scope, so a subtree snapshot's `--prune` can never delete documents outside the
  subtree.
- **Safety** - `apply` respects the global guardrails; it creates/updates by default and requires
  **both** `--prune` and `--yes` to delete. Creates run parent-first, deletes deepest-first, and
  the run stops at the first failure.
- **Out of scope** - property-value references (to media/other content by GUID) are not
  rewritten, so referenced items must already exist in the target; and apply does not move
  existing documents (a placement drift is reported by `diff` as `Drifted` but not applied).
