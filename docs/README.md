# Umbraco.Cli documentation

| If you want to... | Read |
|---|---|
| Install, authenticate, and run your first commands | [getting-started.md](getting-started.md) |
| Drive the CLI from an AI agent or script | [agent-guide.md](agent-guide.md) |
| Look up any command and its options | [commands.md](commands.md) |
| Work on this repository (contribute code) | [../AGENTS.md](../AGENTS.md) and [../CONTRIBUTING.md](../CONTRIBUTING.md) |

## Reference material

- **[adr/](adr/)** - architecture decision records (why the tool is built the way it is).
- **[glossary.md](glossary.md)** and **[../CONTEXT.md](../CONTEXT.md)** - the project's
  ubiquitous language.

## The two fastest sources of truth

Neither of these needs this documentation to be exhaustive:

- `umbraco commands` - the entire command tree as JSON (names, args, options, types,
  required flags, and a `destructive` marker), always in sync with the code.
- `umbraco <command> --schema` - the JSON Schema of a command's `--json-body`.
