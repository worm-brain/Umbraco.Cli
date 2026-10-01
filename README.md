<img src="assets/umbraco-cli.svg" alt="Umbraco CLI logo" width="96" height="96">

# Umbraco.Cli

A command-line tool for [Umbraco CMS](https://umbraco.com/). It talks to Umbraco's Management
API, so you can manage your site's content, media, document types and more from a terminal, a
script or a CI pipeline.

[![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Cli.svg)](https://www.nuget.org/packages/Umbraco.Community.Cli)
[![CI](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml/badge.svg)](https://github.com/worm-brain/Umbraco.Cli/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> Release candidate. Works with Umbraco 17 and later.

## What it does

- **Moves changes between sites.** Export your schema, media or content to a snapshot, see what
  differs on another site, then apply it. Items keep their GUIDs, so the same thing stays the
  same thing everywhere.
- **Runs bulk jobs.** Publish, unpublish or delete a batch of items from a file or a pipe, with a
  result for each one.
- **Fits into scripts.** Every command can print JSON or CSV and returns a meaningful exit code.
  At a terminal you get readable tables instead.
- **Helps you stay safe.** `--readonly`, a command allow-list, confirmation prompts and
  `--dry-run` previews keep automation from surprising you.
- **Covers most of the backoffice.** Content, media, document, media and member types, data
  types, templates, languages, dictionary, members, users, user groups, webhooks, redirects,
  relations and more. [docs/commands.md](docs/commands.md) lists every command.

It runs on Windows, macOS and Linux.

## Install

You'll need the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later and an
Umbraco 17+ site.

```bash
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

That gives you the `umbraco` command. Keep `--prerelease` until 1.0 is out.

Tab completion for bash, zsh and PowerShell is built in: run `umbraco completion <bash|zsh|pwsh>`
and load the output from your shell's startup file
([details](docs/commands.md#shell-completion-completion)).

## Quick start

```bash
# Log in with an API user's client id and secret
umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>

# Check everything is connected
umbraco auth doctor

# List your content
umbraco content list

# Or get JSON for a script
umbraco content list --output json | jq '.data[].name'
```

Not sure where the client id and secret come from?
[Getting started](docs/getting-started.md) walks you through creating an API user.

## Moving schema between sites

```bash
umbraco schema export --out schema.json --profile dev    # snapshot dev's schema
umbraco schema diff schema.json --profile live           # see what differs on live
umbraco schema apply schema.json --profile live          # bring live in line
```

Content and media work the same way with `content export|diff|apply` and
`media export|diff|apply`.

## Documentation

| I want to... | Read |
|---|---|
| Install, log in and run my first commands | [Getting started](docs/getting-started.md) |
| Use the CLI from scripts, CI or an AI agent | [Automation guide](docs/agent-guide.md) |
| Look up a command and its options | [Command reference](docs/commands.md) |
| Compare speed across versions | [Performance](docs/performance.md) |

The CLI can also describe itself: `umbraco commands` prints every command as JSON, and
`umbraco <command> --schema` prints the JSON Schema for a request body. Neither needs a site or
a login.

---

## Building from source

```bash
dotnet build
dotnet test
```

How the code is laid out, and how to work on it, is in [AGENTS.md](AGENTS.md).

## Contributing

Ideas, bug reports and feature requests are very welcome as
[GitHub issues](https://github.com/worm-brain/Umbraco.Cli/issues). Pull requests are by
invitation, so please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening one.

## License

MIT - see [LICENSE](LICENSE).
