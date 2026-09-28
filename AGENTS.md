# AGENTS.md

Guidance for AI agents and human contributors working **in this repository** (editing
the code, opening issues and PRs). This is the canonical instructions file; tool-specific
files such as `CLAUDE.md` redirect here.

> **Using the `umbraco` CLI to drive an Umbraco instance, not editing this repo?**
> You are in the wrong file. Read [`docs/agent-guide.md`](docs/agent-guide.md) - the
> operating manual for agents that call the CLI - and [`docs/commands.md`](docs/commands.md)
> for the full command reference.

---

## Contribution policy (read this before opening a pull request)

Ideas, bug reports, and feature requests are welcome from everyone, human or AI.
**Pull requests are issue-first and invitation-only.** Do not open a PR unless there is an
issue a maintainer has accepted (labelled `accepted` or `help wanted`). Unsolicited
AI-generated PRs for low-value changes (typo sweeps, speculative refactors, reformatting,
drive-by dependency bumps) will be closed unread. The full rules, and the reasoning, are in
[`CONTRIBUTING.md`](CONTRIBUTING.md). Read it first.

---

## What this is

A cross-platform .NET 9 CLI (`umbraco`) for the Umbraco 17+ Management API, shipped as a
NuGet global tool (`Umbraco.Community.Cli`; the command is `umbraco`). It is designed to be
driven both by humans (Spectre.Console tables) and by AI agents/scripts (structured JSON).
JSON is the default output whenever stdout is not a TTY.

## Issue tracking

The repo is hosted at **https://github.com/worm-brain/Umbraco.Cli**. **All issues, to-dos,
bugs, and tasks are tracked as GitHub issues** - there is no in-repo TODO/backlog file. When
you discover a bug or think of an improvement while working, file it as a GitHub issue
(`gh issue create`) rather than leaving a `// TODO` or noting it only in chat. Existing
issues use labels like `bug`, `ci-cd`, `infrastructure`, `architecture`, and `phase-N`;
reuse them for consistency.

## Commands

```bash
dotnet build                                   # build the solution
dotnet test                                    # run all tests
dotnet test --filter "FullyQualifiedName~CommandExecutorTests"   # one test class
dotnet test --filter "Name=RunObject_ApiFailure_WritesErrorAndReturnsOne"  # one test
dotnet pack src/Umbraco.Cli/Umbraco.Cli.csproj -o ./nupkg        # build the tool package
dotnet tool install --global --add-source ./nupkg Umbraco.Community.Cli --prerelease  # install locally to test
dotnet format                                  # or CSharpier - formatting standard for this repo is CSharpier
```

There is no linter beyond the compiler + analyzers; formatting is CSharpier.

### Live integration tests: use the dev site

`tests/Umbraco.Cli.IntegrationTests` runs the built CLI against a live Umbraco instance, and
skips every test when there is none. Don't rely on a hand-built instance (such as
`https://localhost:45000`): use the harness's throwaway dev site, which any machine can create.

```bash
python3 tests/hands-on/dev-site.py test                      # create/start sites/dev, run the suite
python3 tests/hands-on/dev-site.py test --filter "FullyQualifiedName~EffectIntegrationTests"
python3 tests/hands-on/dev-site.py down                      # stop it (keeps its content)
python3 tests/hands-on/dev-site.py env                       # host + credentials, e.g. for scripts/fetch-spec.ps1
```

The first run takes a few minutes: it creates an Umbraco 17 site and fills it with the hands-on
fixture site (content, languages, media, dictionary). Later runs just start it. The suite runs
with its own CLI config (`UMBRACO_TEST_CONFIG`, a single `dev` profile), so it never reads or
writes your personal `umbraco` profiles. On Windows type `python` for `python3`. See
[`tests/hands-on/README.md`](tests/hands-on/README.md).

### Keep the CI SDK and target framework in sync

`*.csproj` target `net9.0` and both CI workflows (`ci.yml`, `publish.yml`) pin
`setup-dotnet` to `9.0.x`. If you bump the target framework, bump the `dotnet-version` in
both workflows to match - an older SDK cannot build a newer target and CI will fail. (This
was the subject of the now-fixed
[issue #35](https://github.com/worm-brain/Umbraco.Cli/issues/35).)

## Architecture

Three projects (`Umbraco.Cli.sln`):

- **`src/Umbraco.Cli`** - the executable: command tree, DI wiring, config, output writers.
- **`src/Umbraco.Cli.Client`** - the typed HTTP client for the Management API. Isolated so
  the generated client can be regenerated when the Umbraco API changes without touching
  command logic. The client is now a Kiota-generated `UmbracoApiClient` (under
  `Generated/`) wrapped by a thin hand-written `UmbracoManagementClient` adapter; see
  [ADR 0003](docs/adr/0003-migrate-write-path-to-generated-client.md).
- **`tests/Umbraco.Cli.Tests`** - xUnit tests.

### The command execution pipeline (the core pattern)

Every API-backed command is built the same way, so to add a command you fill in only what
differs. The flow:

1. **`Program.cs`** builds the DI container and the `System.CommandLine` root command, then
   calls each `XxxCommand.Build(executor)` to attach the subcommand tree. Each top-level noun
   (content, media, users, ...) has a folder under `Commands/` with one `XxxCommand.cs` that
   wires up child verb commands (`list`, `get`, `create`, ...).

2. **`GlobalOptions`** holds the recursive options (`--host`, `--token`, `--output`,
   `--quiet`, `--verbose`, `--config`) as singletons. They are added once to the root and are
   readable off any subcommand's `ParseResult` - so command `Build` methods do not thread
   option instances through their signatures. (Exception: the `auth login`/`logout` commands
   take specific options explicitly because they run *before* auth exists.)

3. A leaf command's `SetAction` delegates to **`CommandExecutor`**, passing: the command name
   (e.g. `"content.list"`), a lambda `(client, ct) => client.SomeApiCall(...)`, and a render
   strategy. Use the right helper:
   - `RunObjectAsync` - serialize the returned object (`WriteSuccess`).
   - `RunPagedAsync` / `RunCompleteListAsync` - a list of entities. Structured output is
     serialized from the items themselves so it matches the matching `get`; the `headers` + `row`
     projection is for the human table only. Never derive JSON keys from a column caption - that
     is what produced `"published": "True"` against `get`'s `"isPublished": true` (#164).
   - `RunListAsync` - the general form, for a list that is not the client's own list type.
   - `RunReportAsync` - a computed report that is complete by construction (`content diff`,
     `schema diff`, #229): the row records are serialized as they are, so it still gets real
     types rather than caption-keyed strings, and `meta` says `hasMore: false`. The apply
     commands write the same way through `CommandExecutor.WriteReport`.
   - `RunMessageAsync` - fixed success message (for delete/publish-style calls).

4. **`CommandExecutor.RunAsync`** is the single place that: builds the `CommandContext` (via
   `CommandContextFactory`), runs the client call, maps a failed `UmbracoResponse<T>` to
   `WriteError` + exit code 1, renders on success, and backstops any unexpected exception into
   a clean error. Exit codes: `0` success, `1` API/unexpected failure, `2` aborted before
   running (no host / not authenticated), `130` cancelled (Ctrl-C).

5. **`CommandContextFactory.CreateAsync`** resolves host + bearer token (precedence:
   `--token` > config/`UMBRACO_CLIENT_*` via OAuth2 client-credentials), configures the
   `HttpClient` base address + auth header, picks the `IOutputWriter`, and throws
   `CommandAbortedException` (after writing the error) when host/credentials are missing.

So: **command files contain almost no logic** - just option definitions and the lambdas.
Cross-cutting behavior (auth, error mapping, exit codes, rendering) lives in
`CommandExecutor` + `CommandContextFactory`. Put new shared behavior there, not in commands.

### Client layer

- **`IUmbracoManagementClient`** is the seam. All commands depend on this interface;
  `IUmbracoManagementClientFactory.Create(HttpClient)` produces it. Tests inject
  `FakeUmbracoManagementClient` via a fake factory - this is what makes commands testable
  without HTTP.
- **`UmbracoManagementClient`** is a thin adapter over the Kiota-generated `UmbracoApiClient`.
  Every call goes through `GuardedAsync`, which converts transport failures (unreachable host,
  timeout, unreadable JSON) into a failed `UmbracoResponse<T>` with `StatusCode = 0` rather
  than throwing. API error bodies are unwrapped to their `title`/`detail` field in
  `BuildErrorAsync`.
- **`UmbracoResponse<T>`** is the uniform envelope (`IsSuccess`, `Data`, `StatusCode`,
  `ErrorMessage`) returned by every client method - never throws for HTTP-level errors.
- **`UmbracoAuthService`** fetches and caches the client-credentials token (thread-safe,
  refreshes at a tenth of the token's lifetime, at most 60 s, before expiry), keyed by host,
  client id and a secret fingerprint. The CLI gives it a `FileTokenCache` so tokens outlive the
  process (#248; off with `UMBRACO_NO_TOKEN_CACHE=1`), and `TokenRefreshHandler` retries a
  request once with a new token on a 401.
- The generated client is produced by Kiota from `spec/management.json` and checked in, so
  building needs no live Umbraco instance. Regenerate with `./scripts/regen-client.ps1` (and
  refresh the spec first with `./scripts/fetch-spec.ps1` if needed). The tested Umbraco range
  lives in `VersionSupport` (the spec only says `"Latest"`): update `MinMajor`/`MaxMajor` when
  the spec moves to a new major.

### Config & output

- **`ConfigStore`** loads/saves `CliConfig` at the OS app-data path
  (`%APPDATA%/Umbraco/config.json` etc.). Environment variables (`UMBRACO_HOST` /
  `UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET`) take precedence over the file, per-field.
- **`OutputWriterFactory`** picks the writer: explicit `--output json|human|csv`, else JSON
  when `Console.IsOutputRedirected`, else human (csv is only ever explicit). `JsonOutputWriter`
  writes the `{status, data, meta}` envelope to stdout and
  `{status, exitCode, httpStatus?, message, category, meta}` errors to **stderr**; `CsvOutputWriter` emits RFC-4180 CSV; `HumanOutputWriter` uses Spectre.Console.
  Table/`--fields` shaping is shared via `OutputShaping` so list and get output agree on
  camelCase field keys across formats. `--quiet` wraps the writer (`QuietOutputWriter`) to drop
  success chatter; `ConsoleColorSetup` honours `NO_COLOR`.

## Conventions specific to this repo

- Adding a command = new `XxxVerbCommand.cs` in the noun's folder + register it in the noun's
  `XxxCommand.Build`. Mirror an existing one (e.g. `Commands/Content/ContentListCommand.cs`)
  rather than inventing a new shape. Then add it to [`docs/commands.md`](docs/commands.md).
  A write declares `.Mutating()` (and `.Destructive(...)` / `.DestructiveWith(...)` if it can lose
  data); its call returns data via `.Then(ItemRef.Of(id))` or `.ThenRead(...)` - a write that
  returns nothing does not compile. `meta.command` is derived from the tree: never type it. Help
  text follows [`docs/conventions.md`](docs/conventions.md) section 8, which `HelpTextTests`
  enforces. That includes checking that every `Examples:` line parses against the tree, using the
  8.5 placeholders, and that `docs/commands.md` names only real command paths and options.
  (Folder and class names such as `ContentTypes/` predate the singular nouns.)
- Errors are data, not exceptions: return `UmbracoResponse.Failure(...)` from the client; let
  `CommandExecutor` translate to exit codes. Do not `Console.WriteLine`/`throw` from commands
  for expected failures.
- Tests that capture stdout/stderr share the `[Collection("ConsoleCapture")]` collection
  because `Console.SetOut/SetError` are process-global - keep new console-capturing tests in
  that collection.
- The machine-readable command catalog (`umbraco commands`) is generated from the command
  tree, so it stays in sync automatically. When you change a command's options or help, the
  catalog updates for free - but the hand-written [`docs/commands.md`](docs/commands.md) does
  not, so update it in the same PR.
- New top-level nouns are registered in `Commands/CliRoot.cs`, the one place the tree is
  assembled.

## CLI surface consistency

The public surface (nouns, verbs, arguments, options, output, error categories, exit codes) must
be consistent. Consistency and the best design for the user outrank any single earlier decision.

Sources of truth, in order:

1. [`docs/conventions.md`](docs/conventions.md) - the design rules.
2. [`docs/surface.json`](docs/surface.json) - the current surface, generated from the command
   tree. `SurfaceSnapshotTests` fails when it is stale; regenerate it with
   `UPDATE_SURFACE=1 dotnet test tests/Umbraco.Cli.Tests --filter SurfaceSnapshotTests` and commit
   it with the change, so every surface change is one reviewable diff.
3. ADRs, plans and issue bodies - history and rationale, not requirements. An issue's plan is a
   snapshot of intent from when it was written.

Rules:

- **Before adding or changing anything on the surface**, read `conventions.md` and find how
  comparable commands already do it in `surface.json`. Match them.
- **If an ADR, plan or issue conflicts with the conventions**, or would make the surface
  inconsistent, don't silently follow it or silently ignore it: name it, say what is outdated,
  and propose the alternative.
- **If doing it properly means changing other commands too**, propose the cross-cutting change and
  list every affected command. No one-off exceptions. Alpha: breaking is fine, inconsistent is not.
- **Bug fixes stay local.** File any cross-cutting surface problem you notice as a GitHub issue
  labelled `api-consistency` and mention it in your summary, rather than widening the fix.
- **You may propose a convention change; only the maintainer accepts one.** An accepted change
  goes in the `conventions.md` changelog with its date and a one-line reason.
- **When asking the maintainer a design question**, first summarise the relevant current surface
  and conventions, and how each option compares to existing commands.

## Domain glossary and decisions

- Ubiquitous language: [`docs/glossary.md`](docs/glossary.md) (and the shorter
  [`CONTEXT.md`](CONTEXT.md)).
- Architecture decisions: [`docs/adr/`](docs/adr/).
