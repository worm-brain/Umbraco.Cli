# Umbraco.Cli documentation

A command-line tool for Umbraco's Management API - built for terminals, scripts, and CI/CD. For
an AI chat assistant that manages Umbraco conversationally, use the first-party
[Umbraco MCP](https://docs.umbraco.com/umbraco-in-ai/mcp) instead.

| If you want to... | Read |
|---|---|
| Install, authenticate, and run your first commands | [getting-started.md](getting-started.md) |
| Automate the CLI in scripts, CI, or an agent | [agent-guide.md](agent-guide.md) |
| Look up any command and its options | [commands.md](commands.md) |
| Acceptance-test a release (agent runbook) | [testing/alpha-test-guide.md](testing/alpha-test-guide.md) |
| Test a build end to end on throwaway local sites (agent runbook) | [../tests/hands-on/README.md](../tests/hands-on/README.md) |
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
