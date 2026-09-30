# Umbraco.Cli documentation

| I want to... | Read |
|---|---|
| Install, log in and run my first commands | [Getting started](getting-started.md) |
| Use the CLI from scripts, CI or an AI agent | [Automation guide](agent-guide.md) |
| Look up a command and its options | [Command reference](commands.md) |
| Compare speed across versions | [Performance](performance.md) |
| Work on the code | [AGENTS.md](../AGENTS.md) and [CONTRIBUTING.md](../CONTRIBUTING.md) |

The CLI can also describe itself, with no site or login needed:

- `umbraco commands` prints every command as JSON: names, arguments, options, types, which are
  required and which are destructive.
- `umbraco <command> --schema` prints the JSON Schema for a command's `--json-body`.
