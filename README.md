# Umbraco.Cli

A cross-platform .NET CLI tool for [Umbraco CMS](https://umbraco.com/), distributed as a NuGet global tool. Drive your Umbraco 14+ instance from the terminal or any AI agent that can call a subprocess.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Cli.svg)](https://www.nuget.org/packages/Umbraco.Community.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Features

- **Full Management API coverage** — content, media, document types, data types, languages, templates, members, member groups, users, dictionary items, webhooks, scripts, stylesheets, partial views, tags, cultures, user groups, user data, document blueprints, server diagnostics, health checks, log viewer, models builder, package manifests, URL redirects, relation types, relations, Examine indexers, Examine searchers, image resize URLs, property-type usage
- **Schema export / diff / apply** — dump document types, data types, and templates to a portable JSON snapshot, diff it against a live instance, and apply the difference (CI/agent-friendly, complements uSync); see [`schema`](#schema-export--diff--apply)
- **Content export / diff / apply** — dump a content subtree to a portable snapshot with stable cross-environment identity, diff it, and reconcile a live instance towards it; see [`content`](#content-export--diff--apply)
- **AI-friendly** — JSON output by default when stdout is not a TTY; consistent envelope with `status`, `data`, and `meta` fields
- **Cross-platform** — Windows, macOS, Linux via .NET 9
- **OAuth2 auth** — Client Credentials stored in an OS-specific config file (see [Configuration](#configuration)); the secret is DPAPI-encrypted at rest on Windows and the file is restricted to your user on macOS/Linux. Environment-variable fallback for CI/CD
- **Human-readable mode** — Spectre.Console tables and colours when running interactively

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Umbraco 14+ instance with the Management API enabled

---

## Installation

The package is published as [`Umbraco.Community.Cli`](https://www.nuget.org/packages/Umbraco.Community.Cli) on NuGet.org (the CLI command is `umbraco`). While the tool is in alpha, pass `--prerelease` so NuGet will resolve the preview versions:

```bash
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

Or as a local tool pinned to a project:

```bash
dotnet new tool-manifest   # once per repo
dotnet tool install Umbraco.Community.Cli --prerelease
```

---

## Quick Start

```bash
# 1. Point the CLI at your instance and authenticate
umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>

# 2. List content
umbraco content list

# 3. Create a content item
umbraco content create --content-type blogPost --name "Hello World"

# 4. Get JSON output for scripting / AI agents
umbraco content list --output json | jq '.data[].name'
```

---

## Authentication

Umbraco.Cli uses [API Users](https://docs.umbraco.com/umbraco-cms/manage-and-publish-content/users-and-members/users/api-users) with the OAuth2 Client Credentials flow.

### Get a client ID and client secret

1. Sign in to Umbraco Backoffice with an administrator account.
2. Go to **Users** and open the **API Users** area.
3. Create a new API user (or open an existing one) and assign the permissions your CLI usage needs.
4. Save the API user and copy the generated **Client ID** and **Client Secret**.
5. Store the secret somewhere safe immediately (password manager/CI secret store), then use both values with `umbraco auth login`.

### Interactive setup

```bash
umbraco auth login
# Prompts for host, client ID, and client secret
# Saved to the OS-specific config file (see Configuration below). The secret is
# DPAPI-encrypted on Windows; the file is owner-only (mode 600) on macOS/Linux.
```

### Non-interactive (CI/CD)

```bash
export UMBRACO_HOST=https://mysite.com
export UMBRACO_CLIENT_ID=umbraco-back-office-my-api-user
export UMBRACO_CLIENT_SECRET=<secret>

umbraco content list --output json
```

### One-off with a raw bearer token

```bash
umbraco content list --token <bearer-token> --host https://mysite.com
```

---

## Command Reference

Global options available on every command:

| Option | Description |
|---|---|
| `--host <url>` | Umbraco instance base URL (overrides config) |
| `--token <bearer>` | Raw bearer token (overrides stored credentials) |
| `--output json\|human\|csv` | Output format (default: `json` when piped, `human` in terminal). `csv` emits RFC-4180 CSV: tables map row-per-line, an object/scalar success flattens to a header+value row, columns use the same camelCase keys as JSON; errors go to stderr as `code,message` |
| `--quiet`, `-q` | Suppress success confirmation messages (e.g. "Deleted."); requested data, errors, and exit codes are still emitted |
| `--verbose` | Log HTTP requests/responses to stderr |
| `--dry-run` | Preview the HTTP request a write command would send (method, URL, body) without executing it; no effect on read commands |
| `--yes`, `-y` | Skip the confirmation prompt on destructive/high-impact commands (delete, empty-recycle-bin, unpublish). Required to run one non-interactively (piped/scripted/agent) |
| `--readonly` | Block all write operations (create/update/delete/publish) for this session; reads still work. Also `UMBRACO_READONLY=1` |
| `--fields <a,b>` | Trim JSON output — or CSV columns — to these top-level fields, in order (e.g. `id,name`), to keep agent context small |
| `--profile <name>`, `-p` | Named credential profile to use (see `auth profiles`); also `UMBRACO_PROFILE`. Defaults to the configured default profile |
| `--config <path>` | Path to config file |

> Colour: ANSI colour in human output is disabled when the `NO_COLOR` environment variable is present (any value, per https://no-color.org), or when stdout is not a TTY.

### Profiles (multiple environments)

Log in to several instances and switch between them without re-authenticating (#64):

```bash
umbraco auth login --profile prod   --host https://prod.example.com   --client-id <id> --client-secret <secret>
umbraco auth login --profile stage  --host https://stage.example.com  --client-id <id> --client-secret <secret>

umbraco auth profiles              # list profiles, * marks the default
umbraco auth use prod              # make 'prod' the default
umbraco content list --profile stage   # use a profile for one command
UMBRACO_PROFILE=stage umbraco content list   # or via env
umbraco auth logout --profile stage    # remove one profile
```

Profiles are stored in the same (owner-only, secret-encrypted) config file. The `UMBRACO_HOST`/`UMBRACO_CLIENT_ID`/`UMBRACO_CLIENT_SECRET` env vars override the selected profile's fields, so CI can still run with pure environment credentials. A pre-profiles (flat) config is migrated automatically to a `default` profile.

> Note: the command allow-list (`allowedCommands`) is stored **per profile**, so it applies only to the profile it was set on. Switching profiles applies that profile's allow-list, but only to *tighten*: the effective list is intersected with the default store's, so a profile switch can never *widen* access (#83). For a hard sandbox, set `UMBRACO_ALLOWED_COMMANDS` in the environment (it applies regardless of profile).

`--fields` keeps only the listed fields on each JSON result (object or array item), in the order given, matched case-insensitively. Under `--output csv` the same list selects and orders the CSV columns:

```bash
umbraco content list --fields id,name
```

Commands that take a `--json-body` also accept `-` to read the body from **stdin**, for clean piping:

```bash
cat body.json | umbraco content create --json-body -
echo '{ "values": [] }' | umbraco content update <id> --json-body -
```

### Agent guardrails

For agent/automation use, two guardrails restrict what a session can do (inspired by the official Umbraco MCP):

- **Read-only mode** — `--readonly` (or `UMBRACO_READONLY=1`) refuses every write with a clear error and a non-zero exit; read commands are unaffected.
- **Command allow-list** — `UMBRACO_ALLOWED_COMMANDS` (or the config `allowedCommands` field) restricts which commands may run. Entries are noun groups (`content`, `media`) and/or specific commands (`content.list`); a command runs only if its group or full name is listed. The `auth` group is always allowed. A blocked command aborts before running with exit `2`.
  - **Unset vs lockdown:** only a *truly unset* value (the variable absent and no config `allowedCommands`) means *no restriction*. Any *present* value that is blank or separators-only — `UMBRACO_ALLOWED_COMMANDS=" "` or `","` — is an **explicit lockdown**: nothing runs but the always-allowed `auth` group. Set an explicit list (e.g. `content,media`) to allow specific groups or commands.

```bash
# An agent that may only read content and media, and never write:
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media umbraco content list
```

> **Enforcement note:** the **environment-variable** forms (`UMBRACO_READONLY`, `UMBRACO_ALLOWED_COMMANDS`) are the enforcement boundary — set them in the parent process that supervises the agent, where the agent cannot change them. The config-file `allowedCommands` form is a convenience/default and is hardened against the obvious footguns — `auth logout` now clears only credentials and **preserves** a profile's allow-list, and a present-but-unreadable config is reported on stderr rather than silently dropping the guardrail — and `--config` / `--profile` can no longer *loosen* it: the effective allow-list is the most-restrictive of the default store's and the resolved one, so pointing `--config` at another file — or selecting a `--profile` defined only there — can only ever *tighten*, never bypass (#83). The environment-variable form remains the recommended boundary for a supervised agent.

Destructive and high-impact commands prompt for confirmation: the permanent `delete` commands, `empty-recycle-bin`, and `content unpublish` / `content bulk unpublish` (which take live content offline). When run non-interactively (no TTY — piped, scripted, or agent-driven) they refuse to proceed unless `--yes` is given, so they can never happen silently. The machine-readable catalog (`umbraco commands`) flags each such command with `"destructive": true` so an agent knows which need `--yes` care.

On a write command, `--dry-run` prints the request that would be sent instead of sending it and exits `0` without changing anything. In JSON mode the envelope uses a distinct `"dry-run"` status (alongside `"success"` and `"error"`):

```jsonc
{ "status": "dry-run", "request": { "method": "POST", "url": ".../umbraco/management/api/v1/webhook", "body": { /* ... */ } } }
```

### `commands` (discover the surface)

`umbraco commands` emits the entire command tree as JSON — every command, its arguments, options, value types, required/optional flags, and one-line help — so an agent or script can discover the whole CLI in a single call instead of scraping `--help`. It is a local command: no host or authentication required. `--output human` prints an indented outline instead.

```bash
umbraco commands | jq '.data.commands[].name'
```

When diffing the catalog across versions, compare `.data` — the envelope's `meta.timestamp` changes on every call.

### `auth`

```bash
umbraco auth login [--host <url>] [--client-id <id>] [--client-secret <secret>]
umbraco auth logout
umbraco auth whoami
```

### `content`

```bash
umbraco content list [--parent <id>] [--skip <n>] [--take <n>]
umbraco content get <id>
umbraco content create --content-type <alias> --name <name> [--json-body <file>]
umbraco content update <id> [--json-body <file>]
umbraco content delete <id>
umbraco content publish <id> [--cultures <csv>]
umbraco content unpublish <id> [--cultures <csv>]         # takes offline; needs --yes
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
umbraco content bulk delete [--file ids.txt]        # permanent; needs --yes
umbraco content bulk publish [--file ids.txt] [--cultures <csv>]
umbraco content bulk unpublish [--file ids.txt] [--cultures <csv>]   # takes offline; needs --yes
```

Bulk commands read ids one per line from `--file` or stdin, so you can pipe:

```bash
umbraco content list --fields id | jq -r '.[].id' | umbraco content bulk publish
```

Each id is reported independently in the `data` results array (`{"id", "status", "error"}`); the exit code is `1` if any item failed. A bulk `delete` is gated by a single confirmation (`--yes` required non-interactively) — it never prompts per item.

All create commands accept an optional `--id <guid>` for **idempotent creates** (Umbraco 14+ honours a client-supplied id), so re-running a provisioning script does not create duplicates.

### `document-blueprint`

Content templates (blueprints) authors start a new document from — keyed off a document type. `get`/`scaffold` return raw JSON (full fidelity); `create`/`update` mirror `content create` (scalar flags OR `--json-body` OR `--schema`).

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
umbraco document-blueprint folder delete <id>             # needs --yes non-interactively
```

### `media`

```bash
umbraco media list [--parent <id>]
umbraco media get <id>
umbraco media upload <file> [--parent <id>] [--name <name>] [--media-type <name|id>]  # staged via temporary-file
umbraco media delete <id>
umbraco media trash <id>                                   # move to recycle bin (reversible)
umbraco media restore <id> [--parent <id>]
umbraco media empty-recycle-bin                            # permanent; needs --yes
umbraco media move <id> [--parent <id>]
```

### `media-types`

```bash
umbraco media-types list
umbraco media-types get <id>
umbraco media-types create --name <name> --alias <alias> [--icon <alias>] [--is-element] [--allow-at-root]
umbraco media-types delete <id>
```

### `content-types`

```bash
umbraco content-types list
umbraco content-types get <id|alias>
umbraco content-types create --name <name> --alias <alias> [--json-body <file>]
umbraco content-types delete <id>
```

### `data-types`

```bash
umbraco data-types list
umbraco data-types get <id|alias>
umbraco data-types create --name <name> --editor-alias <alias> --editor-ui-alias <alias>
umbraco data-types update <id> --name <name> --editor-alias <alias> --editor-ui-alias <alias>
umbraco data-types delete <id>
umbraco data-types is-used <id>                            # whether any content type uses it
umbraco data-types referenced-by <id> [--skip <n>] [--take <n>]   # raw JSON; mixed reference kinds
umbraco data-types copy <id> [--target <folder>]          # omit --target to copy to the root; needs --yes non-interactively
umbraco data-types move <id> [--target <folder>]          # omit --target to move to the root; needs --yes non-interactively

# folder sub-noun (organise data types in the tree):
umbraco data-types folder get <id>
umbraco data-types folder create --name <name> [--parent <folder>] [--id <guid>]
umbraco data-types folder update <id> --name <name>
umbraco data-types folder delete <id>                     # needs --yes non-interactively
```

### `languages`

```bash
umbraco languages list
umbraco languages create --culture <code> [--default]
umbraco languages update <iso-code> --name <name> [--default] [--mandatory] [--fallback <code>]
umbraco languages delete <iso-code>
```

### `templates`

```bash
umbraco templates list
umbraco templates get <alias>
umbraco templates create --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco templates update <id> --name <name> --alias <alias> [--content <razor> | --content-file <file>]
umbraco templates delete <id>
```

### `members`

```bash
umbraco members list [--group <name>]
umbraco members get <id|email>
umbraco members create --email <email> --name <name> --type <alias>
umbraco members update <id> [--email <email>] [--name <name>] [--approved]
umbraco members delete <id>
```

### `member-types`

```bash
umbraco member-types list
umbraco member-types get <id>
umbraco member-types create --name <name> --alias <alias> [--icon <alias>]
umbraco member-types update <id> [--name <name>] [--alias <alias>] [--description <desc>] [--icon <alias>]
umbraco member-types delete <id>
```

### `users`

```bash
umbraco users list
umbraco users get <id|email>
umbraco users invite --email <email> --name <name>
```

### `user-groups`

```bash
umbraco user-groups list
umbraco user-groups get <id>
umbraco user-groups create --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--language <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access] [--media-root-access] [--id <guid>]
umbraco user-groups update <id> --alias <alias> --name <name> [--icon <alias>] [--description <text>] [--section <alias>]... [--language <iso>]... [--fallback-permission <perm>]... [--has-access-to-all-languages] [--document-root-access] [--media-root-access]
umbraco user-groups delete <id>
umbraco user-groups delete-many --ids <id>...              # bulk; needs --yes non-interactively
umbraco user-groups add-users <id> --user <id>...          # --user repeatable
umbraco user-groups remove-users <id> --user <id>...       # --user repeatable
```

Granular per-node permissions are a deferred follow-up: `create`/`update`
set the scalar and list fields but send an empty permissions set. `update`
replaces the whole group, so pass the full desired state.

### `user-data`

Key/value data scoped to the **authenticated** user.

```bash
umbraco user-data list [--group <group>] [--identifier <id>] [--skip <n>] [--take <n>]
umbraco user-data get <key>
umbraco user-data create --group <group> --identifier <id> --value <value> [--key <guid>]
umbraco user-data update --key <key> --group <group> --identifier <id> --value <value>
umbraco user-data delete <key>
```

### `dictionary`

```bash
umbraco dictionary list
umbraco dictionary get <key>
umbraco dictionary create --key <key> [--values en=Hello --values da=Hej]
umbraco dictionary delete <id>
```

### `webhooks`

```bash
umbraco webhooks list
umbraco webhooks create --url <url> --events <csv> [--name <name>] [--description <text>]
umbraco webhooks delete <id>
```

### `script` / `stylesheet` / `partial-view` (static files)

The three static-file resources share the same path-addressed verbs (the noun is
`script`, `stylesheet`, or `partial-view`). Files are identified by **path** (not a GUID);
`update` replaces the content only.

```bash
umbraco script list [--parent <folder>]                    # tree root, or a folder's children
umbraco script get <path>                                  # includes the file content
umbraco script create --name <file> [--parent <folder>] [--content <text> | --content-file <file>]
umbraco script update <path> [--content <text> | --content-file <file>]
umbraco script delete <path>                               # needs --yes non-interactively

# stylesheet and partial-view take exactly the same verbs, e.g.:
umbraco stylesheet create --name site.css --content-file ./site.css
umbraco partial-view get grid/row.cshtml
```

### `member-groups`

```bash
umbraco member-groups list
umbraco member-groups get <id>
umbraco member-groups create --name <name>
umbraco member-groups update <id> --name <name>
umbraco member-groups delete <id>
```

### `tags` / `cultures` (read-only)

```bash
umbraco tags list [--group <group>] [--culture <iso>]      # tags, with node counts
umbraco cultures list                                      # available cultures (isoCode + name)
```

### `server` (read-only)

```bash
umbraco server status                                      # runtime status
umbraco server info                                        # version + runtime mode
umbraco server configuration                               # public config flags
umbraco server troubleshooting                             # troubleshooting items (name/value)
```

### `health`

```bash
umbraco health list [--skip <n>] [--take <n>]              # health-check groups
umbraco health get <group>                                 # a group and the checks it contains
umbraco health run <group>                                 # run the group, return results (POST, so blocked by --readonly)
```

### `log-viewer`

```bash
umbraco log-viewer log [--level <Verbose|Debug|Information|Warning|Error|Fatal>]... [--filter <expr>] [--start-date <date>] [--end-date <date>] [--skip <n>] [--take <n>] [--ascending]
umbraco log-viewer levels [--skip <n>] [--take <n>]        # loggers and their minimum levels
umbraco log-viewer level-count [--start-date <date>] [--end-date <date>]   # message counts by level
umbraco log-viewer message-templates [--skip <n>] [--take <n>] [--start-date <date>] [--end-date <date>]

# saved-search sub-noun:
umbraco log-viewer saved-search list [--skip <n>] [--take <n>]
umbraco log-viewer saved-search create --name <name> --query <query>
umbraco log-viewer saved-search delete <name>             # needs --yes non-interactively
```

`--level` is a typed enum (`Verbose`/`Debug`/`Information`/`Warning`/`Error`/`Fatal`); repeat it for several levels. An unknown value is rejected at parse time.

Serilog filter expressions commonly start with `@` (e.g. `@Level='Error'`, `@Exception is not null`). These are taken literally — the CLI disables `@file` response-file expansion — so no escaping is needed:

```bash
umbraco log-viewer log --filter "@Level='Error'"
umbraco log-viewer saved-search create --name Errors --query "@Level='Error'"
```

### `models-builder`

```bash
umbraco models-builder dashboard                           # dashboard status
umbraco models-builder status                              # whether generated models are out of date
umbraco models-builder build                               # regenerates source files on the server; needs --yes non-interactively (POST, so blocked by --readonly)
```

### `manifest` (read-only)

```bash
umbraco manifest list [--scope All|Public|Private]         # default: All
```

### `redirect`

```bash
umbraco redirect list [--content <key>] [--filter <s>] [--skip <n>] [--take <n>]   # --content lists redirects to that document (key ignores --filter)
umbraco redirect status                                    # whether automatic URL-redirect tracking is enabled
umbraco redirect delete <id>                               # needs --yes non-interactively
umbraco redirect tracking enable                           # site-wide toggle; needs --yes non-interactively
umbraco redirect tracking disable                          # site-wide toggle; needs --yes non-interactively
```

### `relation-type` (read-only)

```bash
umbraco relation-type list [--skip <n>] [--take <n>]
umbraco relation-type get <id>
```

### `relation` (read-only)

```bash
umbraco relation list --type <relationTypeId> [--skip <n>] [--take <n>]   # relations are listed only by relation-type id
```

### `indexer`

```bash
umbraco indexer list [--skip <n>] [--take <n>]             # Examine indexes, with health + document counts
umbraco indexer get <name>
umbraco indexer rebuild <name>                             # expensive; needs --yes non-interactively (POST, so blocked by --readonly)
```

### `searcher` (read-only)

```bash
umbraco searcher list [--skip <n>] [--take <n>]
umbraco searcher query <name> --term <term> [--skip <n>] [--take <n>]
```

### `imaging` (read-only)

```bash
umbraco imaging resize-urls --id <guid>... [--width <px>] [--height <px>] [--mode <Crop|Max|Stretch|Pad|BoxPad|Min>] [--format <fmt>]   # --id repeatable
```

### `property-type` (read-only)

```bash
umbraco property-type is-used --content-type <id> --alias <alias>
```

### `schema` (export / diff / apply)

Dump the site's **schema** — document types, data types, and templates — to a portable JSON
snapshot, diff it against a live instance, and apply the difference. Driven from any shell or
agent; complements uSync for CI pipelines. (Issue #68; see [ADR 0005](docs/adr/0005-schema-export-diff-apply.md).)

```bash
# Export every document type, data type, and template to a snapshot file.
umbraco schema export --out schema.json

# See what differs between a snapshot and the live instance (read-only; empty == in sync).
umbraco schema diff schema.json
umbraco schema export | umbraco schema diff -          # pipe an export straight into a diff

# Preview the full apply plan without writing anything.
umbraco schema apply schema.json --dry-run

# Reconcile the instance towards the snapshot (create + update; never deletes by default).
umbraco schema apply schema.json

# Also delete live entities absent from the snapshot (destructive; requires --yes non-interactively).
umbraco schema apply schema.json --prune --yes
```

How it works:

- **Fidelity** — the snapshot stores each entity's **verbatim Management-API body**, so nothing
  is lost (document-type properties/compositions, data-type configuration values, template
  Razor). The snapshot is `{ schemaVersion, documentTypes[], dataTypes[], templates[] }`.
- **Matching** — diff/apply pair a snapshot entity to a live one by **id first, then human key**
  (alias for document types/templates, name for data types), so a snapshot is idempotent against
  its own instance and portable to another. An id-only-vs-key match is flagged `idMismatch`.
- **Safety** — `apply` respects the global guardrails: `--dry-run` previews the whole plan and
  writes nothing, `--readonly` (or `UMBRACO_READONLY=1`) blocks it, and the destructive `--prune`
  requires confirmation / `--yes`. Writes run in dependency order (data types -> templates ->
  document types, topologically sorted within each) and stop at the first failure.
- **Scope** — this covers schema (document types, data types, templates). The parallel pipeline
  for content is documented below under [`content (export / diff / apply)`](#content-export--diff--apply).

### `content` (export / diff / apply)

The content pipeline mirrors the schema one for documents (issue #100, ADR 0006), so the same
`export` -> `diff` -> `apply` workflow moves content between environments and detects drift.

```bash
umbraco content export --out content.json                 # whole content tree
umbraco content export --root <id> --out subtree.json     # a subtree (root included)
umbraco content diff content.json                         # read-only
umbraco content apply content.json --dry-run              # preview the whole plan
umbraco content apply content.json                        # create + update
umbraco content apply content.json --prune --yes          # also delete what the snapshot omits
```

- **Full fidelity** — each document is stored as its verbatim Management-API body, so nothing is
  lost (all variants, all property values). A document's raw body does not carry its parent, so the
  snapshot records placement separately: `{ contentVersion, root, documents[] }` where each entry is
  `{ id, parent, body }`, in tree pre-order (parents before children).
- **Identity** — documents are matched by **GUID only** (they have no stable natural key). Apply
  recreates a document with its snapshot GUID (Umbraco 14+ honours a client-supplied id), so the
  same content has the same identity in every environment.
- **Scope-safe prune** — the snapshot records its export `root`, and diff/apply compare against the
  same live scope, so a subtree snapshot's `--prune` can never delete documents outside the subtree.
- **Safety** — `apply` respects the global guardrails; it creates/updates by default and requires
  **both** `--prune` and `--yes` to delete. Creates run parent-first, deletes deepest-first, and the
  run stops at the first failure.
- **Out of scope** — property-value references (to media/other content by GUID) are not rewritten, so
  referenced items must already exist in the target; and apply does not move existing documents (a
  placement drift is reported by `diff` as `Drifted` but not applied).

---

## JSON Output

When stdout is not a TTY (piped or redirected), JSON is the default output format. Every command returns the same envelope:

```json
{
  "status": "success",
  "data": { },
  "meta": {
    "command": "content.list",
    "durationMs": 142,
    "schemaVersion": "2"
  }
}
```

Errors are written to **stderr**:

```json
{ "status": "error", "code": 404, "message": "Content item not found" }
```

### Envelope contract (`meta.schemaVersion`)

The success envelope is versioned via `meta.schemaVersion` (currently `"2"`). The field names above (`status`, `data`, `meta`, `command`, `durationMs`, `schemaVersion`, and the error envelope's `code`/`message`) are part of the contract: they are **never renamed silently**. `schemaVersion` is bumped only on a **breaking** change — a renamed/removed field or a changed meaning. New fields may be added without a bump, so consumers should ignore unknown fields. Agents can gate on `meta.schemaVersion` and diff `.data` between runs (`meta.timestamp` changes every call).

**v2** switched list (table) JSON keys from the human header text to camelCase — e.g. a `content-types list` item is now `{"id": ..., "name": ..., "alias": ..., "isElement": ...}` instead of `{"ID": ..., "Content Type": ...}` — so the same field uses the same key whether it comes from a `list` or a `get`.

### CSV output (`--output csv`)

`--output csv` emits RFC-4180 CSV instead of the JSON envelope. A list result becomes one CSV row per item; an object or scalar success flattens to a two-line header+value CSV. Column headers use the same camelCase keys as JSON output, and `--fields` projects and orders the columns just as it does for JSON. Errors are written to **stderr** as a single `code,message` line.

### `--schema` (request body shape)

Commands that accept a `--json-body` expose `--schema`, which prints the JSON Schema of that body and exits — no host or authentication needed. The output is a **bare JSON Schema document** (not the CLI envelope), regardless of `--output`, so you can feed it straight to a validator or an agent. `--schema` is a local describe-and-exit and is not subject to the allow-list or `--readonly`.

```bash
umbraco content create --schema
umbraco content update --schema
```

Exit codes: `0` success · `1` API error, or an invalid invocation (parse/validation error) · `2` aborted before running (no host / not authenticated, a command blocked by the allow-list, a destructive command refused/declined without `--yes`, or a write blocked by `--readonly`) · `130` cancelled (Ctrl-C)

---

## Configuration File

Stored automatically after `umbraco auth login`:

- **Linux**: `~/.config/Umbraco/config.json`
- **macOS**: `~/Library/Application Support/Umbraco/config.json`
- **Windows**: `%APPDATA%\Umbraco\config.json`

Override with the `--config <path>` flag or the `UMBRACO_HOST` / `UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET` environment variables.

---

## Development

### Prerequisites

- .NET 9 SDK
- A running Umbraco 14+ instance for integration testing

### Build

```bash
dotnet build
```

### Regenerate the API client

The typed client under `src/Umbraco.Cli.Client/Generated` is generated by
[Kiota](https://learn.microsoft.com/openapi/kiota/) from the OpenAPI document
committed at `spec/management.json`, and is checked in so building needs no live
Umbraco instance. To refresh it (requires the Kiota global tool,
`dotnet tool install --global Microsoft.OpenApi.Kiota`):

```powershell
# Optional: pull a fresh spec from a running instance first
./scripts/fetch-spec.ps1 -Host https://localhost:44300 -SkipCertificateCheck

# Regenerate the client from spec/management.json
./scripts/regen-client.ps1
```

`UmbracoManagementClient` is a thin hand-written adapter over the generated
`UmbracoApiClient`: it keeps the `UmbracoResponse<T>` envelope, transport-failure
guard and command-facing DTOs stable so command code never sees the generated
types.

### Test

```bash
dotnet test
```

### Pack locally

```bash
dotnet pack src/Umbraco.Cli/Umbraco.Cli.csproj -o ./nupkg
dotnet tool install --global --add-source ./nupkg Umbraco.Community.Cli --prerelease
```

---

## Architecture

```
Umbraco.Cli/           — executable, commands, DI wiring
Umbraco.Cli.Client/    — Kiota-generated HTTP client + thin UmbracoManagementClient adapter
Umbraco.Cli.Tests/     — unit & integration tests
```

The client project is isolated so the generated client can be regenerated when the Umbraco API changes (see [Regenerate the API client](#regenerate-the-api-client)) without touching command logic.

---

## Roadmap

- [x] Project scaffold & planning
- [x] Phase 1: Auth + infrastructure (OAuth2, config store, DI, output writers)
- [x] Phase 2: Content, media, document types
- [x] Phase 3: Languages, templates, members, users, dictionary, webhooks
- [x] Phase 4: Comprehensive help text, NuGet metadata
- [ ] Phase 5: GitHub Actions CI, NuGet.org publish

---

## Issue tracking & contributing

The repository lives at **https://github.com/worm-brain/Umbraco.Cli**.

**All bugs, to-dos, feature requests, and other tasks are tracked as [GitHub issues](https://github.com/worm-brain/Umbraco.Cli/issues).** There is no separate backlog or TODO file — if it needs doing, it should exist as an issue. When you find a bug or think of an improvement, open an issue (or file it with the [`gh`](https://cli.github.com/) CLI: `gh issue create`). Pull requests should reference the issue they close.

---

## License

MIT — see [LICENSE](LICENSE).
