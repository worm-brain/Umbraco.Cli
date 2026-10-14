# Umbraco.Cli

A command-line tool for [Umbraco CMS](https://umbraco.com/). It talks to Umbraco's Management
API, so you can manage your site's content, media, document types and more from a terminal, a
script or a CI pipeline.

> Release candidate. Works with Umbraco 17 and later.

## What it does

- **Moves changes between sites.** Export your schema, media or content to a snapshot, see what
  differs on another site, then apply it.
- **Runs bulk jobs.** Publish, unpublish or delete a batch of items from a file or a pipe.
- **Fits into scripts.** Every command can print JSON or CSV and returns a meaningful exit code.
- **Helps you stay safe.** `--readonly`, a command allow-list, confirmation prompts and
  `--dry-run` previews keep automation from surprising you.

## Install

You'll need the .NET 10 SDK or later and an Umbraco 17+ site.

```bash
dotnet tool install -g Umbraco.Community.Cli --prerelease
```

That gives you the `umbraco` command.

## Quick start

```bash
umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>
umbraco auth doctor
umbraco content list
```

## Documentation

- [Getting started](https://github.com/worm-brain/Umbraco.Cli/blob/main/docs/getting-started.md)
- [Automation guide](https://github.com/worm-brain/Umbraco.Cli/blob/main/docs/agent-guide.md)
- [Command reference](https://github.com/worm-brain/Umbraco.Cli/blob/main/docs/commands.md)
- [Source and issues](https://github.com/worm-brain/Umbraco.Cli)
