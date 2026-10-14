# Umbraco.Community.Cli

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

- [Documentation](https://github.com/worm-brain/Umbraco.Cli/blob/main/docs/index.md): getting
  started, the automation guide for scripts, CI and AI agents, and the command reference
- [Configuration](https://github.com/worm-brain/Umbraco.Cli/blob/main/docs/reference/configuration.md):
  profiles, the config file and environment variables
- [Changelog](https://github.com/worm-brain/Umbraco.Cli/blob/main/CHANGELOG.md)
- [Source](https://github.com/worm-brain/Umbraco.Cli#readme) and
  [issues](https://github.com/worm-brain/Umbraco.Cli/issues)
