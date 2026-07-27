# Umbraco.Cli

A cross-platform .NET CLI tool for [Umbraco CMS](https://umbraco.com/), distributed as a NuGet global tool. Drive your Umbraco 14+ instance from the terminal or any AI agent that can call a subprocess.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Cli.svg)](https://www.nuget.org/packages/Umbraco.Community.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Features

- **Full Management API coverage** — content, media, document types, data types, languages, templates, members, users, dictionary items, webhooks
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
| `--config <path>` | Path to config file |

On a write command, `--dry-run` prints the request that would be sent instead of sending it and exits `0` without changing anything. In JSON mode the envelope uses a distinct `"dry-run"` status (alongside `"success"` and `"error"`):

```jsonc
{ "status": "dry-run", "request": { "method": "POST", "url": ".../umbraco/management/api/v1/webhook", "body": { /* ... */ } } }
```

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
umbraco content unpublish <id> [--cultures <csv>]
```

### `media`

```bash
umbraco media list [--parent <id>]
umbraco media get <id>
umbraco media upload <file> --parent <id> --name <name>
umbraco media delete <id>
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
```

### `languages`

```bash
umbraco languages list
umbraco languages create --culture <code> [--default]
umbraco languages delete <iso-code>
```

### `templates`

```bash
umbraco templates list
umbraco templates get <alias>
```

### `members`

```bash
umbraco members list [--group <name>]
umbraco members get <id|email>
umbraco members create --email <email> --name <name> --type <alias>
umbraco members delete <id>
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
```

### `webhooks`

```bash
umbraco webhooks list
umbraco webhooks create --url <url> --events <csv>
umbraco webhooks delete <id>
```

---

## JSON Output

When stdout is not a TTY (piped or redirected), JSON is the default output format. Every command returns the same envelope:

```json
{
  "status": "success",
  "data": { },
  "meta": {
    "command": "content.list",
    "durationMs": 142
  }
}
```

Errors are written to **stderr**:

```json
{ "status": "error", "code": 404, "message": "Content item not found" }
```

Exit codes: `0` success · `1` API error · `2` auth error · `3` argument error

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
