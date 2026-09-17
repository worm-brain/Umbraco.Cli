# Umbraco.Cli

A cross-platform .NET CLI tool for [Umbraco CMS](https://umbraco.com/), distributed as a NuGet
global tool. Drive your Umbraco 14+ instance from the terminal or any AI agent that can call a
subprocess.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Cli.svg)](https://www.nuget.org/packages/Umbraco.Community.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> Status: alpha, published on NuGet.org. The CLI covers the Umbraco 14+ Management API surface
> and is built to be driven by AI agents as well as people.

---

## What this is

- **Full Management API coverage** - content, media, document/media/member/data types,
  languages, templates, members, users, dictionary, webhooks, static files (scripts,
  stylesheets, partial views), tags, cultures, user groups, redirects, relations, Examine,
  diagnostics (server, health, log viewer, models builder, manifest), and more. Every command
  is listed in [docs/commands.md](docs/commands.md).
- **Schema and content sync** - export document types / data types / templates (and content
  subtrees) to a portable JSON snapshot, diff it against a live instance, and apply the
  difference. CI- and agent-friendly; complements uSync.
- **Built for AI agents** - JSON by default when stdout is not a TTY; a single versioned
  `{status, data, meta}` envelope; a self-describing surface (`umbraco commands`, `--schema`);
  and guardrails (`--readonly`, a command allow-list, `--yes`, `--dry-run`). See the
  [agent guide](docs/agent-guide.md).
- **Cross-platform** - Windows, macOS, Linux via .NET 9.
- **Human-readable mode** - Spectre.Console tables and colour when running interactively.

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later.
- An Umbraco 14+ instance with the Management API enabled.

## Install

The package is [`Umbraco.Community.Cli`](https://www.nuget.org/packages/Umbraco.Community.Cli)
on NuGet.org; the command it installs is `umbraco`. While the tool is in alpha, pass
`--prerelease`:

```bash
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

## Quick start

```bash
# 1. Point the CLI at your instance and authenticate
umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>

# 2. Confirm the setup (checks host, TLS, credentials, auth, identity, version)
umbraco auth doctor

# 3. List content
umbraco content list

# 4. JSON for scripting / AI agents
umbraco content list --output json | jq '.data[].name'
```

Full setup (including how to create the API user and its client id/secret) is in
[docs/getting-started.md](docs/getting-started.md).

---

## Documentation

| I want to... | Go to |
|---|---|
| Install, authenticate, run first commands | [docs/getting-started.md](docs/getting-started.md) |
| Drive the CLI from an AI agent or script | [docs/agent-guide.md](docs/agent-guide.md) |
| Look up any command and its options | [docs/commands.md](docs/commands.md) |
| See the full docs map | [docs/README.md](docs/README.md) |
| Contribute to this repository | [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md) |

The two most machine-friendly sources of truth ship inside the tool itself:
`umbraco commands` (the whole command tree as JSON) and `umbraco <command> --schema` (a
request body's JSON Schema). Neither needs a host or authentication.

---

## Architecture

```
src/Umbraco.Cli/         - executable: commands, DI wiring, config, output writers
src/Umbraco.Cli.Client/  - Kiota-generated HTTP client + thin UmbracoManagementClient adapter
tests/Umbraco.Cli.Tests/ - unit and integration tests
```

The client project is isolated so the generated client can be regenerated when the Umbraco API
changes without touching command logic. The full architecture, the command-execution pipeline,
and the build/test/regenerate workflow are documented in [AGENTS.md](AGENTS.md).

```bash
dotnet build     # build
dotnet test      # run the tests
```

---

## Contributing

Ideas, bug reports, and feature requests are welcome from everyone - humans and AI agents alike
- as [GitHub issues](https://github.com/worm-brain/Umbraco.Cli/issues). Pull requests are
issue-first and invitation-only; please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening
one. All work is tracked as GitHub issues - there is no separate backlog or TODO file.

## License

MIT - see [LICENSE](LICENSE).
