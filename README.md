# Umbraco.Cli

A cross-platform .NET CLI tool for [Umbraco CMS](https://umbraco.com/), distributed as a NuGet global tool. Drive your Umbraco 14+ instance from the terminal or any AI agent that can call a subprocess.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Cli.svg)](https://www.nuget.org/packages/Umbraco.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Features

- **Full Management API coverage** — content, media, document types, data types, languages, templates, members, users, dictionary items, webhooks
- **AI-friendly** — JSON output by default when stdout is not a TTY; consistent envelope with `status`, `data`, and `meta` fields
- **Cross-platform** — Windows, macOS, Linux via .NET 9
- **Secure auth** — OAuth2 Client Credentials stored in `~/.umbraco/config.json`; environment variable fallback for CI/CD
- **Human-readable mode** — Spectre.Console tables and colours when running interactively

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Umbraco 14+ instance with the Management API enabled

---

## Installation

```bash
dotnet tool install -g Umbraco.Cli
```

Or as a local tool pinned to a project:

```bash
dotnet new tool-manifest   # once per repo
dotnet tool install Umbraco.Cli
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
# Credentials are stored in the OS credential store
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
| `--token <bearer>` | Raw bearer token (overrides credential store) |
| `--output json\|human` | Output format (default: `json` when piped, `human` in terminal) |
| `--verbose` | Log HTTP requests/responses to stderr |
| `--config <path>` | Path to config file |

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

```bash
kiota generate \
  --language csharp \
  --openapi "http://localhost:5000/umbraco/swagger/management/swagger.json" \
  --output "src/Umbraco.Cli.Client/Generated" \
  --namespace-name "Umbraco.Cli.Client" \
  --class-name "UmbracoManagementClient"
```

### Test

```bash
dotnet test
```

### Pack locally

```bash
dotnet pack src/Umbraco.Cli/Umbraco.Cli.csproj -o ./nupkg
dotnet tool install --global --add-source ./nupkg Umbraco.Cli
```

---

## Architecture

```
Umbraco.Cli/           — executable, commands, DI wiring
Umbraco.Cli.Client/    — Kiota-generated HTTP client (regenerate from OpenAPI spec)
Umbraco.Cli.Tests/     — unit & integration tests
```

The client project is isolated so it can be regenerated when the Umbraco API changes without touching command logic.

---

## Roadmap

See the [GitHub Project board](https://github.com/worm-brain/Umbraco.Cli/projects) for current status.

- [x] Project scaffold & planning
- [x] Phase 1: Auth + infrastructure (OAuth2, config store, DI, output writers)
- [x] Phase 2: Content, media, document types
- [x] Phase 3: Languages, templates, members, users, dictionary, webhooks
- [x] Phase 4: Comprehensive help text, NuGet metadata
- [ ] Phase 5: GitHub Actions CI, NuGet.org publish

---

## License

MIT — see [LICENSE](LICENSE).
