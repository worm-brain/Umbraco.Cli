# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A cross-platform .NET 9 CLI (`umbraco`) for the Umbraco 14+ Management API, shipped as a NuGet global tool. Designed to be driven both by humans (Spectre.Console tables) and by AI agents/scripts (structured JSON). JSON is the default output whenever stdout is not a TTY.

## Issue tracking

The repo is hosted at **https://github.com/worm-brain/Umbraco.Cli**. **All issues, to-dos, bugs, and tasks are tracked as GitHub issues** in that repository — there is no in-repo TODO/backlog file. When you discover a bug or think of an improvement while working, file it as a GitHub issue (`gh issue create`) rather than leaving a `// TODO` or noting it only in chat. Existing issues use labels like `bug`, `ci-cd`, `infrastructure`, `architecture`, and `phase-N`; reuse them for consistency.

## Commands

```bash
dotnet build                                   # build the solution
dotnet test                                    # run all tests
dotnet test --filter "FullyQualifiedName~CommandExecutorTests"   # one test class
dotnet test --filter "Name=RunObject_ApiFailure_WritesErrorAndReturnsOne"  # one test
dotnet pack src/Umbraco.Cli/Umbraco.Cli.csproj -o ./nupkg        # build the tool package
dotnet tool install --global --add-source ./nupkg Umbraco.Community.Cli --prerelease  # install locally for manual testing (NuGet package id; command is `umbraco`)
dotnet format                                  # or CSharpier — formatting standard for this repo is CSharpier
```

There is no linter beyond the compiler + analyzers; formatting is CSharpier.

### Keep the CI SDK and target framework in sync

`*.csproj` target `net9.0` and both CI workflows (`ci.yml`, `publish.yml`) pin `setup-dotnet` to `9.0.x`. If you bump the target framework, bump the `dotnet-version` in both workflows to match — an older SDK cannot build a newer target and CI will fail. (This was the subject of the now-fixed [issue #35](https://github.com/worm-brain/Umbraco.Cli/issues/35).)

## Architecture

Three projects (`Umbraco.Cli.sln`):

- **`src/Umbraco.Cli`** — the executable: command tree, DI wiring, config, output writers.
- **`src/Umbraco.Cli.Client`** — the typed HTTP client for the Management API. Deliberately isolated so it can be swapped for a Kiota-generated client without touching command logic (see the `kiota generate` command in the README / the doc comment on `IUmbracoManagementClient`). The current implementation is hand-written.
- **`tests/Umbraco.Cli.Tests`** — xUnit tests.

### The command execution pipeline (the core pattern)

Every API-backed command is built the same way, so to add a command you fill in only what differs. The flow:

1. **`Program.cs`** builds the DI container and the `System.CommandLine` root command, then calls each `XxxCommand.Build(executor)` to attach the subcommand tree. Each top-level noun (content, media, users, ...) has a folder under `Commands/` with one `XxxCommand.cs` that wires up child verb commands (`list`, `get`, `create`, ...).

2. **`GlobalOptions`** holds the recursive options (`--host`, `--token`, `--output`, `--quiet`, `--verbose`, `--config`) as singletons. They are added once to the root and are readable off any subcommand's `ParseResult` — so command `Build` methods don't thread option instances through their signatures. (Exception: the `auth login`/`logout` commands take specific options explicitly because they run *before* auth exists.)

3. A leaf command's `SetAction` delegates to **`CommandExecutor`**, passing: the command name (e.g. `"content.list"`), a lambda `(client, ct) => client.SomeApiCall(...)`, and a render strategy. Use the right helper:
   - `RunObjectAsync` — serialize the returned object (`WriteSuccess`).
   - `RunTableAsync` — project the result into table rows (`headers` + a `rows` selector).
   - `RunMessageAsync` — fixed success message (for delete/publish-style calls).

4. **`CommandExecutor.RunAsync`** is the single place that: builds the `CommandContext` (via `CommandContextFactory`), runs the client call, maps a failed `UmbracoResponse<T>` to `WriteError` + exit code 1, renders on success, and backstops any unexpected exception into a clean error. Exit codes: `0` success, `1` API/unexpected failure, `2` aborted before running (no host / not authenticated), `130` cancelled (Ctrl-C).

5. **`CommandContextFactory.CreateAsync`** resolves host + bearer token (precedence: `--token` > config/`UMBRACO_CLIENT_*` via OAuth2 client-credentials), configures the `HttpClient` base address + auth header, picks the `IOutputWriter`, and throws `CommandAbortedException` (after writing the error) when host/credentials are missing.

So: **command files contain almost no logic** — just option definitions and the three lambdas. Cross-cutting behavior (auth, error mapping, exit codes, rendering) lives in `CommandExecutor` + `CommandContextFactory`. Put new shared behavior there, not in commands.

### Client layer

- **`IUmbracoManagementClient`** is the seam. All commands depend on this interface; `IUmbracoManagementClientFactory.Create(HttpClient)` produces it. Tests inject `FakeUmbracoManagementClient` via a fake factory — this is what makes commands testable without HTTP.
- **`UmbracoManagementClient`** wraps `HttpClient`. Every call goes through `GuardedAsync`, which converts transport failures (unreachable host, timeout, unreadable JSON) into a failed `UmbracoResponse<T>` with `StatusCode = 0` rather than throwing. API error bodies are unwrapped to their `title`/`detail` field in `BuildErrorAsync`.
- **`UmbracoResponse<T>`** is the uniform envelope (`IsSuccess`, `Data`, `StatusCode`, `ErrorMessage`) returned by every client method — never throws for HTTP-level errors. `Models.cs` holds all request/response records with `[JsonPropertyName]` mappings.
- **`UmbracoAuthService`** fetches and caches the client-credentials token (thread-safe, refreshes 5 min before expiry).

### Config & output

- **`ConfigStore`** loads/saves `CliConfig` at the OS app-data path (`%APPDATA%/Umbraco/config.json` etc.). Environment variables (`UMBRACO_HOST`/`UMBRACO_CLIENT_ID`/`UMBRACO_CLIENT_SECRET`) take precedence over the file, per-field.
- **`OutputWriterFactory`** picks the writer: explicit `--output json|human|csv`, else JSON when `Console.IsOutputRedirected`, else human (csv is only ever explicit). `JsonOutputWriter` writes the `{status, data, meta}` envelope to stdout and `{status, code, message}` errors to **stderr**; `CsvOutputWriter` emits RFC-4180 CSV; `HumanOutputWriter` uses Spectre.Console. Table/`--fields` shaping is shared via `OutputShaping` so list and get output agree on camelCase field keys across formats. `--quiet` wraps the writer (`QuietOutputWriter`) to drop success chatter; `ConsoleColorSetup` honours `NO_COLOR`.

## Conventions specific to this repo

- Adding a command = new `XxxVerbCommand.cs` in the noun's folder + register it in the noun's `XxxCommand.Build`. Mirror an existing one (e.g. `Commands/Content/ContentListCommand.cs`) rather than inventing a new shape.
- Errors are data, not exceptions: return `UmbracoResponse.Failure(...)` from the client; let `CommandExecutor` translate to exit codes. Don't `Console.WriteLine`/`throw` from commands for expected failures.
- Tests that capture stdout/stderr share the `[Collection("ConsoleCapture")]` collection because `Console.SetOut/SetError` are process-global — keep new console-capturing tests in that collection.
