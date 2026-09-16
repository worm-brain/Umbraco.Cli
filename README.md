# Umbraco.Cli

A cross-platform .NET CLI tool for [Umbraco CMS](https://umbraco.com/), distributed as a NuGet global tool. Drive your Umbraco 14+ instance from the terminal or any AI agent that can call a subprocess.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Cli.svg)](https://www.nuget.org/packages/Umbraco.Community.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Features

- **Full Management API coverage** — content, media, document types, data types, languages, templates, members, users, dictionary items, webhooks
- **Schema export / diff / apply** — dump document types, data types, and templates to a portable JSON snapshot, diff it against a live instance, and apply the difference (CI/agent-friendly, complements uSync); see [`schema`](#schema-export--diff--apply)
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
| `--output json\|human` | Output format (default: `json` when piped, `human` in terminal) |
| `--verbose` | Log HTTP requests/responses to stderr |
| `--dry-run` | Preview the HTTP request a write command would send (method, URL, body) without executing it; no effect on read commands |
| `--yes`, `-y` | Skip the confirmation prompt on destructive/high-impact commands (delete, empty-recycle-bin, unpublish). Required to run one non-interactively (piped/scripted/agent) |
| `--readonly` | Block all write operations (create/update/delete/publish) for this session; reads still work. Also `UMBRACO_READONLY=1` |
| `--fields <a,b>` | Trim JSON output to these top-level fields, in order (e.g. `id,name`), to keep agent context small |
| `--profile <name>`, `-p` | Named credential profile to use (see `auth profiles`); also `UMBRACO_PROFILE`. Defaults to the configured default profile |
| `--config <path>` | Path to config file |

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

> Note: the command allow-list (`allowedCommands`) is stored **per profile**, so it applies only to the profile it was set on — switching profiles uses that profile's allow-list. For a hard sandbox, set `UMBRACO_ALLOWED_COMMANDS` in the environment (it applies regardless of profile).

`--fields` keeps only the listed fields on each JSON result (object or array item), in the order given, matched case-insensitively:

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
  - **Empty vs deny-all:** an unset or empty value (`UMBRACO_ALLOWED_COMMANDS=""`) means *no restriction*; a value with only separators (e.g. `","`) means *deny everything except `auth`*. Set an explicit list to restrict.

```bash
# An agent that may only read content and media, and never write:
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media umbraco content list
```

> **Enforcement note:** the **environment-variable** forms (`UMBRACO_READONLY`, `UMBRACO_ALLOWED_COMMANDS`) are the enforcement boundary — set them in the parent process that supervises the agent, where the agent cannot change them. The config-file `allowedCommands` form is a convenience/default and is hardened against the obvious footguns — `auth logout` now clears only credentials and **preserves** a profile's allow-list, and a present-but-unreadable config is reported on stderr rather than silently dropping the guardrail — but a process that controls its own arguments could still point `--config` at a different file, so treat the file form as advisory, not a hard sandbox.

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
umbraco member-types delete <id>
```

### `users`

```bash
umbraco users list
umbraco users get <id|email>
umbraco users invite --email <email> --name <name>
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
- **Scope** — this first slice covers schema only; content export/diff/apply is tracked as
  follow-up issues.

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
