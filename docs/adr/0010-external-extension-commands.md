# ADR 0010: External extension commands and the guarded API passthrough

- Status: Accepted
- Date: 2026-09-30
- Issue: #438, part of epic #437. Related: ADR 0007 (allow-list), ADR 0001 (contract tests).

## Context

A package that adds its own Management API routes (a hypothetical `Umbraco.Foo` with a
`foo export`) has no way to ship commands that feel like part of `umbraco`. Epic #437 rejects
loading third-party assemblies into the CLI: it would freeze pre-1.0 internals as a public API,
invite dependency conflicts inside a global tool, and put third-party code in a process that
holds client secrets.

The alternative is a separate executable the package author ships as their own dotnet tool,
which the CLI runs by name. That adds process execution, which `docs/security-audit.md` tracked
as absent ("no process execution anywhere"). An extension also needs to call the site, and
`docs/agent-guide.md` warned that a direct HTTP call skips every guardrail.

## Decision

### 1. An unknown top-level noun runs `umbraco-<noun>` from PATH

`umbraco foo bar --x 1` runs `umbraco-foo bar --x 1` when that executable is on PATH, and the
CLI exits with its exit code.

- **Built-in nouns always win.** A noun the command tree has is never looked up on PATH, so an
  extension cannot shadow `content` or `api`.
- **Exact names only.** The noun must be lower-case kebab-case (`^[a-z][a-z0-9-]*$`), like every
  built-in noun, and the file must be named exactly `umbraco-<noun>`. A noun can therefore never
  carry a path separator, a `..` or a shell metacharacter into the file name.
- **No shell, ever.** On Windows only an `.exe` is accepted: a `.cmd` or `.bat` needs `cmd.exe`
  to run it. Elsewhere the file must have an execute bit. The executable is started directly
  by its full path, never through a shell.
- **Only absolute PATH entries are searched.** A relative entry (`.`, or an empty one) would run
  whatever the current directory holds.
- **Arguments go as a list,** one by one, never joined into a string, so nothing is re-split or
  interpreted.
- **stdin, stdout and stderr are inherited,** so the extension talks to the user directly. The
  CLI writes nothing on success; it writes its own error envelope only when it refuses to launch.
- **The exit code passes through,** so `130` (cancelled) and the rest mean the same as for a
  built-in. Ctrl-C reaches the extension too; the CLI waits for it rather than exiting first.
- **When nothing matches,** the line is parsed as before and fails with today's parse-error
  envelope, unchanged.

The one process-execution sink is `ExtensionProcess.RunAsync`
(`src/Umbraco.Cli/Commands/Extensions/`); `ExtensionLocator` holds the naming and PATH rules.

### 2. The context options belong to the CLI; everything else is the extension's

The options that say where and how requests run - `--host`, `--token`, `--config`, `--profile`,
`--output`, `--readonly`, `--dry-run` - are the CLI's wherever they appear before a `--`, as
they are on a built-in command. They are taken out of the extension's arguments. Everything else
reaches the extension in order: its own arguments, the other global options (`--yes`, `--quiet`,
`--verbose`, `--fields`) and `--help`. A `--` ends the CLI's options; it and what follows are
passed unchanged.

Taking the context options out means `umbraco foo purge --dry-run` previews the extension's
writes whether or not the extension knows the option. Leaving them in would make every guardrail
the user typed depend on each extension honouring it. `--yes` is left to the extension: an
environment variable that confirms destructive commands would be one a supervisor could not
safely leave unset.

### 3. The context reaches the extension's calls through environment variables

An extension calls back into the CLI for HTTP (section 4), and those calls run in the context of
the line that launched it. The launcher adds variables to the extension's environment, which it
otherwise inherits whole (so `UMBRACO_ALLOWED_COMMANDS` and an operator's `UMBRACO_READONLY`
reach it unchanged). Every command reads each variable as the option's default; an option on the
command line still wins.

| Option | Variable | Existed before |
|---|---|---|
| `--profile` | `UMBRACO_PROFILE` | yes |
| `--readonly` | `UMBRACO_READONLY=1` | yes |
| `--host` | `UMBRACO_HOST` | yes |
| `--config` | `UMBRACO_CONFIG` (made absolute) | new |
| `--dry-run` | `UMBRACO_DRY_RUN=1` | new |
| `--output` | `UMBRACO_OUTPUT` | new |
| `--token` | `UMBRACO_TOKEN` | new |

Only options the line gave are set, so nothing can clear a variable the environment already
holds, and the tightening ones (`--readonly`, `--dry-run`) can only be added. The new variables
work on every command, not only on calls from an extension: that keeps one meaning per variable,
and it means an extension that calls a built-in command gets the same context as one that calls
`umbraco api`.

**`--host` is checked before it becomes `UMBRACO_HOST`.** `UMBRACO_HOST` is trusted as the
operator's setting, so the credentials it resolves with are sent to it, while a `--host` naming
another instance is refused unless `--token` comes with it (conventions 7). A child cannot tell
an inherited `UMBRACO_HOST` from a configured one, so the launcher applies the `--host` rule
itself: a `--host` the configured credentials do not belong to, without `--token`, is refused
(exit `2`, `refused`) before the extension starts.

**A token leaves the CLI only when the user passed it.** A token the CLI obtains from client
credentials is never given to an extension: its calls obtain their own, which the token cache
makes free. A token given with `--token` is the user's, and the extension's calls cannot
authenticate without it, so it is passed as `UMBRACO_TOKEN`. That is the one way a bearer token
reaches an extension process, and the user chose it by typing `--token` on the extension's line.

### 4. `umbraco api`: a guarded passthrough

`umbraco api <method> <path>` sends one request to the site and prints the response in the
standard envelope. It runs through `CommandExecutor` like every built-in command, so host and
auth resolution, token refresh, `--readonly`, `--dry-run`, the allow-list and the error envelope
(with Umbraco's ProblemDetails as `details`) all apply. The body is `--json-body <file|->`, the
conventions' name for a request body.

- **One verb per method:** `api get`, `api post`, `api put`, `api patch`, `api delete`. Safety is
  declared per command (conventions 5.2): `get` is a read, `post`/`put`/`patch` are writes,
  `delete` is destructive and needs `--yes` non-interactively. A single command taking the method
  as an argument could not declare its safety, would report one `mutating` value in
  `umbraco commands` for reads and writes alike, and would give the allow-list one name for all
  of them. With a verb per method, a list can permit `api.get` without permitting writes.
- **The site's own `/umbraco/` routes only.** The path is from the host root and must start
  `/umbraco/`: the Management API, the Delivery API or a package's own routes. A full URL, a
  protocol-relative path, a `.`/`..` segment (or an encoded dot), a backslash or a fragment is
  refused, at parse time and again in the client before anything is sent, so the passthrough
  cannot become a general HTTP client for the bearer token.
- **The response body is the envelope's `data`;** `{}` when there is none (a create answers 201
  with no body, so an extension should put the id in the body it creates with, as the CLI does);
  a body that is not JSON is kept as a string.
- **No `--schema`.** The body's shape is whatever the path takes, which the CLI has no schema
  for; the catalog's `jsonBodySchema` pointer is left out for these verbs. Recorded as a known
  exception in `docs/conventions.md`.

The client seam gains `IPassthroughClient.SendRawAsync`, built on the same Kiota adapter as the
schema pipeline's raw-JSON helpers.

### 5. Allow-list: the extension's noun is its own group

`UMBRACO_ALLOWED_COMMANDS=content,foo` permits `umbraco foo ...` as it would a built-in `foo`
noun; a list without `foo` refuses it (exit `2`, `not_allowed`) before it starts. Full-name
entries such as `foo.export` do not apply to extensions: the CLI cannot see an extension's
subcommands.

An extension's calls back into the CLI are ordinary commands and are checked as such: an
`umbraco api get` it makes needs `api` or `api.get` in the list. So a supervisor that allows an
extension decides separately what it may do on the site, for example
`UMBRACO_ALLOWED_COMMANDS=content,foo,api.get` for an extension that only reads.

Rejected: letting an extension's `umbraco api` calls ride on the extension's own group, signalled
by a variable the launcher sets. Anything that can set a variable could forge it, which would let
a file-based allow-list be widened from the environment, against ADR 0007's rule that every source
only tightens.

### 6. Catalog

`umbraco commands` lists the extensions on PATH after the built-in commands, marked
`"external": true`, with the executable's path in the description. It never runs them, so an
extension's arguments and options are not listed; its own `--help` describes it. The committed
surface (`docs/surface.json`) is the built-in tree alone, the same on every machine.

### 7. Contract tests exempt the passthrough, and only it

ADR 0001's contract tests fail any Management API request the spec does not declare. A
passthrough request carries the caller's path, which may be a package's route under
`/umbraco/management/api/` that the core spec cannot know. `SendRawAsync` marks its requests with
a Kiota request option (`PassthroughRequestOption`), and the test handlers skip only marked
requests. An unmarked undeclared request still fails, and a test pins both.

## Trust model

An extension is code the user chose to install, like any dotnet tool, and it runs with the
user's rights. It can read their files, including the CLI's config, so the CLI does not try to
contain it. The guardrails here protect against mistakes - a typo that would shadow a built-in,
a `--dry-run` the extension forgot to honour, a `--host` pointed at the wrong site - not against
a malicious extension.

## Consequences

- `docs/security-audit.md`'s filesystem and process lens names this as the one deliberate process
  sink; a new process start anywhere else is a finding.
- A package author ships `umbraco-foo` as their own tool, versioned with their package. The guide
  for them is `docs/extension-commands.md`.
- `UMBRACO_CONFIG`, `UMBRACO_DRY_RUN`, `UMBRACO_OUTPUT` and `UMBRACO_TOKEN` are new environment
  variables on every command.
- An extension that makes HTTP calls under an allow-list needs the `api` verbs it uses listed
  too.
- Global option names are reserved: an extension must not define its own `--host`, `--config` and
  so on, because the CLI takes them.
- An extension calls whichever `umbraco` is on PATH. A development build launching an extension
  may therefore have its calls answered by the installed CLI.

## Follow-on

- **Discovery hint (`commandTool`).** A site could name a package's tool in its `umbracoCli`
  manifest declaration (ADR 0009), so the CLI can say "this site has Foo; installing
  `Umbraco.Foo.Cli` adds its commands" when that tool is not on PATH. It needs the capabilities
  reader from #440 and is left for after that lands.
