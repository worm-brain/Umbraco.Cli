# Umbraco.Cli

A cross-platform .NET CLI for [Umbraco CMS](https://umbraco.com/)'s Management API, distributed
as a NuGet global tool. Built for terminals, shell scripts, and CI/CD - promote schema and
content between environments, run bulk jobs, and get structured JSON or CSV out of every command.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Cli.svg)](https://www.nuget.org/packages/Umbraco.Community.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> Status: alpha, published on NuGet.org. Works against Umbraco 17+ (including v18).

## Umbraco.Cli or the official Umbraco MCP?

Umbraco ships a first-party [MCP server](https://docs.umbraco.com/umbraco-in-ai/mcp) (Node,
version-locked to the CMS) that exposes the Management API as tools for **AI chat agents** -
Claude, Cursor, Copilot and the like. If you want an assistant to manage Umbraco
conversationally, use that.

Umbraco.Cli is a **command-line tool** for a different job - terminals, shell scripts, and
CI/CD. Reach for it to

- **promote schema, media and content between environments** (`export -> diff -> apply` with
  drift detection, keeping GUIDs),
- **run bulk operations** over many items from a file or a pipe,
- **script the Management API** with deterministic JSON/CSV, exit codes, and Unix piping,
- **stay in .NET** with no Node runtime (`dotnet tool install`).

An AI agent that prefers calling a subprocess can drive it too (see the
[agent guide](docs/agent-guide.md)) - it just isn't the tool's reason to exist.

---

## What this is

- **Environment sync** - export the schema (document, media and member types, data types,
  templates, languages, dictionary, member and user groups), media with its files, and content
  subtrees to portable snapshots, diff them against a live instance, and apply the difference
  with the same GUIDs. Drift detection and environment promotion for CI; complements uSync.
- **Bulk operations** - publish, unpublish, or delete many items from a file or stdin, each
  reported independently with its own status.
- **Structured, scriptable output** - one versioned `{status, data, meta}` JSON envelope
  (a `--dry-run` preview uses `{status, request, meta}` instead), RFC-4180 CSV, `--fields`
  projection, and documented exit codes - plus Spectre.Console tables when you are at a
  terminal.
- **Guardrails** - `--readonly`, a command allow-list, `--yes` confirmations, and `--dry-run`
  request previews for safe automation.
- **Broad Management API coverage** - content, media, document/media/member/data types,
  languages, templates, members, users, dictionary, webhooks, static files, tags, cultures,
  user groups, redirects, relations, Examine, and diagnostics (server, health, log viewer,
  models builder, manifest). Every command is in [docs/commands.md](docs/commands.md). Writes
  cover domains, media folders, member groups and passwords, dictionary updates, and authoring
  document types and data types in full via `--json-body`. Reads return the full item.
- **.NET-native and cross-platform** - Windows, macOS, Linux via .NET 9; no Node runtime.

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later.
- An Umbraco 17+ instance with the Management API enabled.

## Install

The package is [`Umbraco.Community.Cli`](https://www.nuget.org/packages/Umbraco.Community.Cli)
on NuGet.org; the command it installs is `umbraco`. While the tool is in alpha, pass
`--prerelease`:

```bash
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

Tab completion for bash, zsh and PowerShell comes with it: `umbraco completion <bash|zsh|pwsh>`
prints the script to load from your shell's startup file (see
[Shell completion](docs/commands.md#shell-completion-completion)).

## Quick start

```bash
# 1. Point the CLI at your instance and authenticate
umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>

# 2. Confirm the setup (checks host, TLS, credentials, auth, identity, version)
umbraco auth doctor

# 3. List content
umbraco content list

# 4. Structured JSON for scripting and CI
umbraco content list --output json | jq '.data[].name'
```

Full setup (including how to create the API user and its client id/secret) is in
[docs/getting-started.md](docs/getting-started.md).

---

## Documentation

| I want to... | Go to |
|---|---|
| Install, authenticate, run first commands | [docs/getting-started.md](docs/getting-started.md) |
| Automate the CLI in scripts, CI, or an agent | [docs/agent-guide.md](docs/agent-guide.md) |
| Look up any command and its options | [docs/commands.md](docs/commands.md) |
| See the full docs map | [docs/README.md](docs/README.md) |
| Contribute to this repository | [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md) |

The two most machine-friendly sources of truth ship inside the tool itself:
`umbraco commands` (the whole command tree as JSON) and `umbraco <command> --schema` (a
request body's JSON Schema). Neither needs a host or authentication.

---

<!-- perf:start -->
<!-- Generated by `python3 tests/hands-on/bench.py report` from docs/performance/results/. Don't edit between the perf markers: record a new run with bench.py, or change the text in tests/hands-on/tools/perf_report.py, then regenerate. -->

## Performance

The CLI wraps Umbraco's Management API, so most of a command's time is Umbraco's; the CLI's own share is shown
separately. CI gates request counts and allocations, never timings.

| Command | End-to-end ms, including Umbraco server time | CLI overhead ms | HTTP requests |
|---|--:|--:|--:|
| Startup: `umbraco --version` | 113.4 +/- 1.9 | 113.4 | 0 |
| Read one item: `umbraco content get <home>` | 341.5 +/- 9.5 | 274.5 | 4 |
| Export the content tree: `umbraco content export --out <file>` | 411.1 +/- 11.5 | 271.6 | 34 |

Measured 2026-09-29 on AMD Ryzen 9 7950X 16-Core Processor, 32 logical cores, Windows 11 (10.0.26200): CLI
0.1.0-alpha.15 (.NET 9.0.20) against Umbraco 17.7.0 on the same machine (the test harness's dev site). Mean
+/- standard deviation of 10 hyperfine runs. CLI overhead is end-to-end minus the time in HTTP requests:
startup, auth, parsing, the CLI's own work and output. Note on this run: Recorded on a busy machine (other
workloads, including a BenchmarkDotNet run, ran alongside), so expect a wider spread than on a quiet machine.

Method, history across CLI versions, caveats and how to reproduce every number:
[docs/performance.md](docs/performance.md).
<!-- perf:end -->

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
