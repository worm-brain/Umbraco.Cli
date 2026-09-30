# Writing an extension command

A package can add its own commands to `umbraco`. When someone runs `umbraco foo export --all`
and the CLI has no `foo` command, it runs an executable called `umbraco-foo` from PATH with
`export --all`, and exits with its exit code. You ship that executable yourself, usually as a
.NET tool from your package's repository, versioned with your package.

This page is for package authors. The design and its reasons are in
[ADR 0010](adr/0010-external-extension-commands.md).

## Name it `umbraco-<noun>`

- The file name is exactly `umbraco-` followed by your noun. The noun is lower-case letters,
  digits and hyphens, starting with a letter: `umbraco-foo`, `umbraco-rich-text`.
- On Windows the file must be an `.exe`. A `.cmd`, `.bat` or script is not run: the CLI never
  starts a shell. On Linux and macOS it must be an executable file with no extension.
- A .NET tool gets both for free. Set the command name in your tool's project file:

  ```xml
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>umbraco-foo</ToolCommandName>
  <PackageId>Umbraco.Foo.Cli</PackageId>
  ```

  Users install it with `dotnet tool install -g Umbraco.Foo.Cli`, and `umbraco foo` works.
- Built-in commands always win. Pick a noun no built-in command uses (see `umbraco commands`);
  your package's name is the safest choice. A noun a later CLI release adds would take over from
  your tool, so avoid generic words.

`umbraco commands` lists your command with `"external": true` and the path it runs, without
running it. Your own `--help` is what describes it: `umbraco foo --help` runs `umbraco-foo --help`.

## What you receive

Your executable gets the rest of the command line, in order, with one exception: the options
that say where and how requests run belong to the CLI wherever they appear, and are taken out.

| Taken out, and passed to you as | Option |
|---|---|
| `UMBRACO_HOST` | `--host`, `-H` |
| `UMBRACO_TOKEN` | `--token` |
| `UMBRACO_CONFIG` (an absolute path) | `--config` |
| `UMBRACO_PROFILE` | `--profile`, `-p` |
| `UMBRACO_OUTPUT` (`json`, `human` or `csv`) | `--output`, `-o` |
| `UMBRACO_READONLY=1` | `--readonly` |
| `UMBRACO_DRY_RUN=1` | `--dry-run` |

So `umbraco foo export --all --dry-run -o json` runs `umbraco-foo export --all` with
`UMBRACO_DRY_RUN=1` and `UMBRACO_OUTPUT=json` added to its environment. You inherit the rest of
the environment as it is, including `UMBRACO_ALLOWED_COMMANDS` and any variable the user already
set.

Everything else is yours, including the other global options: `--yes`, `--quiet`, `--verbose`,
`--fields` and `--help`. A `--` stops the CLI taking options out; it and everything after it
reach you unchanged.

Do not define options with the global names (`--host`, `--config`, `--output` and the rest): the
CLI takes them before you see them. Put your own options after your noun.

You do not need to act on `UMBRACO_DRY_RUN` or `UMBRACO_READONLY` yourself: every call you make
through `umbraco api` honours them (below). Read them if you want to say so in your output.

## Call the site through `umbraco api`

Make every HTTP call by running `umbraco api`. It sends the request with the user's login and
applies every guardrail a built-in command has: `--readonly` refuses writes, `--dry-run` previews
them, the allow-list applies, and an expired token is renewed. Your process never needs the
user's credentials, and you never receive a token the CLI obtained. Do not read the CLI's config
file or ask for client credentials.

```bash
umbraco api get /umbraco/management/api/v1/foo/items -o json
umbraco api post /umbraco/management/api/v1/foo/items --json-body item.json -o json
cat item.json | umbraco api put /umbraco/management/api/v1/foo/items/<id> --json-body - -o json
umbraco api delete /umbraco/management/api/v1/foo/items/<id> --yes -o json
```

- The verb is the HTTP method: `get`, `post`, `put`, `patch` or `delete`.
- The path is from the site's root and starts `/umbraco/`, with any query string. Your package's
  own routes are fine; nothing outside `/umbraco/`, and no other host, can be called.
- `--json-body <file|->` is the request body, sent as it is.
- **Pass `-o json` whenever you read the result.** Without it the call follows the user's
  `--output`, which is right when you let its output go straight to the user and wrong when you
  parse it.
- The response body is `.data` in the envelope on stdout. A response with no body is `{}`. A
  create usually answers with no body, so put the new item's id in the body you send, and you
  will know it.
- A failure is an error envelope on stderr and a non-zero exit code. Umbraco's own error body is
  in `.details`.
- `delete` asks for confirmation, and refuses without `--yes` when it cannot ask. Pass `--yes` to
  it only when the user gave `--yes` to you.
- Call it as `umbraco`, from PATH, with its arguments as a list. Do not build one command string
  for a shell to split.

In C#:

```csharp
using System.Diagnostics;
using System.Text.Json;

static (int Exit, JsonElement? Data) Api(params string[] args)
{
    var start = new ProcessStartInfo("umbraco") { RedirectStandardOutput = true };
    foreach (var arg in args)
        start.ArgumentList.Add(arg);
    start.ArgumentList.Add("-o");
    start.ArgumentList.Add("json");

    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    // stderr is not redirected, so an error envelope reaches the user as it is.
    return process.ExitCode == 0
        ? (0, JsonDocument.Parse(stdout).RootElement.GetProperty("data").Clone())
        : (process.ExitCode, null);
}

var (exit, items) = Api("api", "get", "/umbraco/management/api/v1/foo/items");
if (exit != 0)
    return exit;
```

## Allow-lists

A supervisor can restrict a session with `UMBRACO_ALLOWED_COMMANDS` or a config
`allowedCommands` list. Your noun is a group of its own, as a built-in noun is: `foo` in the list
permits `umbraco foo ...`, and a list without it refuses your command before it starts.

Your calls back into the CLI are checked as the commands they are. A command that reads needs
`api.get` (or `api`) in the list as well; one that writes needs the write verbs. Say so in your
README, for example: "under an allow-list, add `foo,api.get`".

## Print the standard envelope

Scripts and agents read every `umbraco` command the same way, so print what a built-in command
prints. With `UMBRACO_OUTPUT=json`, or no `UMBRACO_OUTPUT` and stdout not a terminal, write JSON:

```json
{
  "status": "success",
  "data": { "exported": 12 },
  "meta": {
    "command": "foo.export",
    "timestamp": "2026-09-30T12:00:00Z",
    "durationMs": 840,
    "schemaVersion": "6"
  }
}
```

- `data` is always present: the item or list you produced, or `{ "id": ... }` for a write that
  acted on one item.
- `meta.command` is your command path with dots, starting with your noun.
- `meta.schemaVersion` is the envelope version of the CLI release you built against; an
  `umbraco api` result's `meta.schemaVersion` tells you the installed CLI's.
- Errors go to **stderr**, as
  `{ "status": "error", "exitCode": 1, "message": "...", "category": "...", "meta": { ... } }`.
  Use the CLI's categories: `invalid_argument` for input the caller must fix, `request_rejected`
  and `server_error` for the site's answers, `not_allowed`, `readonly`,
  `confirmation_required`, `refused`, `cancelled`, `unreachable`, `timeout`,
  `unexpected_response`, `internal`.
- A thin command that runs one `umbraco api` call can let that call write straight to stdout and
  stderr, and return its exit code: its envelope is already the standard one.

With `UMBRACO_OUTPUT=human`, or stdout a terminal, print something readable instead. With `csv`,
print CSV, or say you do not support it.

## Exit codes

Exit with the codes every `umbraco` command uses; the CLI passes yours on unchanged.

| Exit | Meaning |
|---|---|
| `0` | Success, including a `--dry-run` preview. |
| `1` | The command ran and failed: a site error, invalid input, an unexpected error. |
| `2` | Stopped before running: not authenticated, refused by the allow-list or `--readonly`, confirmation missing. |
| `130` | Cancelled (Ctrl-C). |

When an `umbraco api` call fails with `2` (the allow-list, `--readonly`, a missing `--yes`),
stop and exit `2` too, so the caller sees why.

## Checklist

- [ ] The executable is `umbraco-<noun>`, an `.exe` on Windows, and installs onto PATH.
- [ ] `umbraco <noun> --help` describes your commands and options.
- [ ] No option of yours reuses a global option's name.
- [ ] Every HTTP call goes through `umbraco api`, with `-o json` where you read the result.
- [ ] JSON output is the standard envelope; errors go to stderr with a category.
- [ ] Exit codes follow the table above.
- [ ] Your README names the allow-list entries your command needs.
