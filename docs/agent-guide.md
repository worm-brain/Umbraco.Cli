# Agent guide

The operating manual for an AI agent (or any script) that drives the `umbraco` CLI. If you are
a human setting the tool up, start with [getting-started.md](getting-started.md); if you are
looking for a specific command, see [commands.md](commands.md).

> **This CLI or the official Umbraco MCP?** For an AI assistant that manages Umbraco
> conversationally (Claude, Cursor, Copilot), use Umbraco's first-party
> [MCP server](https://docs.umbraco.com/umbraco-in-ai/mcp) - it is version-locked to the CMS and
> exposes the Management API as MCP tools. This guide is for the other case: an agent or script
> that drives the `umbraco` CLI as a **subprocess** inside automation or CI, where you want
> deterministic JSON, exit codes, and shell composability.

Two properties make this CLI cheap to drive programmatically:

1. **Everything is JSON.** When stdout is not a TTY, every command emits the same envelope.
2. **The CLI describes itself.** You can discover the entire surface, and the exact shape of
   every request body, without a live server and without scraping `--help`.

Read the two discovery sections first - they let you operate the tool without this document
being exhaustive.

> **Before you rely on a command, check
> [section 8, Known limits and escape hatches](#8-known-limits-and-escape-hatches).** A few
> commands still report success for something that did not happen, and several reads return less
> than their help suggests. That section lists each one with the workaround.

---

## 1. Discover the surface: `umbraco commands`

`umbraco commands` prints the whole command tree as JSON in one call: every command, its
arguments, options, value types, required/optional flags, one-line help, and a `destructive`
flag on commands that need care. It is a **local** command - no host, no authentication.

```bash
umbraco commands | jq '.data.commands[].name'         # top-level nouns
umbraco commands | jq '.. | .name? // empty'          # every command name
```

Prefer this over hard-coding command knowledge. When you diff the catalog across CLI versions,
compare `.data` only - `meta.timestamp` changes on every call.

## 2. Discover request bodies: `--schema`

Any command that accepts a `--json-body` also accepts `--schema`, which prints the JSON Schema
of that body and exits. It is local (no host/auth), it ignores `--output` (always a bare JSON
Schema document, not the envelope), and it is never blocked by the allow-list or `--readonly`.

```bash
umbraco content create --schema        # the shape of a content-create body
umbraco content update --schema
```

Feed the schema straight to a validator, or use it to construct a valid body before sending.

---

## 3. The output contract

Every successful command emits this envelope on **stdout**:

```json
{
  "status": "success",
  "data": { },
  "meta": { "command": "content.list", "durationMs": 142, "schemaVersion": "2" }
}
```

Errors go to **stderr**:

```json
{ "status": "error", "code": 404, "message": "Content item not found" }
```

An error from an Umbraco API call also carries a `category` and, when the server responded, the
`serverVersion` - so you can tell **whose** problem it is without a controlled experiment:

```json
{
  "status": "error",
  "code": 500,
  "message": "The Umbraco server returned an internal error (HTTP 500). This is a server-side problem, not a rejected request; check the Umbraco logs.",
  "category": "server_error",
  "serverVersion": "17.3.5"
}
```

`category` is one of: `unreachable` (no response - DNS/connection), `timeout`, `request_rejected`
(a 4xx - usually bad input or the request itself), `server_error` (a 5xx or an undeclared status -
a server-side fault), or `unexpected_response` (the body did not match what the CLI expected, a
likely version mismatch). `serverVersion` is omitted when the server could not be reached
(`unreachable`/`timeout`) or the version could not be determined. Policy errors that never hit the
API (auth, `--readonly`, cancellation) carry neither field.

A write command run with `--dry-run` uses a distinct status and does not touch the server:

```json
{ "status": "dry-run", "request": { "method": "POST", "url": ".../webhook", "body": { } } }
```

### Contract stability rules

- The field names above (`status`, `data`, `meta`, `command`, `durationMs`, `schemaVersion`,
  the error `code`/`message`, and the error `category`/`serverVersion`) are part of the contract
  and are never renamed silently.
- `meta.schemaVersion` (currently `"2"`) is bumped **only** on a breaking change - a renamed or
  removed field, or a changed meaning. New fields can appear without a bump.
- Therefore: **ignore unknown fields**, and if you want to be defensive, gate on
  `meta.schemaVersion`.
- `list` results use the same camelCase keys as the corresponding `get`, so a field has the
  same name wherever it appears (this is what schemaVersion 2 established).

> **Known exception, being fixed.** Some `list` commands do not yet honour that last rule.
> List output is currently projected from the human table columns, so its values are
> **strings** and a few of its keys differ from the matching `get`: `content list` gives
> `"published": "True"` where `content get` gives `"isPublished": true`, and `languages list`
> gives `"default"`/`"mandatory"` against `create`/`update`'s `isDefault`/`isMandatory`. Do not
> write `jq 'select(.published)'` against a list result until this is closed -
> compare strings, or read the item with `get`.
> ([#164](https://github.com/worm-brain/Umbraco.Cli/issues/164))

## 4. Output formats and trimming

- **JSON** is the default whenever stdout is redirected/piped. Force it anywhere with
  `--output json`.
- **`--fields a,b`** keeps only those top-level fields, in order, on each result (object or
  array item), matched case-insensitively. Use it to keep your context small:
  ```bash
  umbraco content list --fields id,name
  ```
- **`--output csv`** emits RFC-4180 CSV (list -> one row per item; object/scalar -> header+value).
  Columns use the same camelCase keys, and `--fields` selects/orders them. Errors go to stderr
  as a `code,message` line.
- **`--quiet` / `-q`** drops success-confirmation chatter ("Deleted.") but still emits requested
  data, errors, and exit codes.

## 5. Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including a `--dry-run` preview). |
| `1` | API error, or an invalid invocation (parse/validation error). |
| `2` | Aborted before running: no host / not authenticated, blocked by the allow-list, a destructive command refused without `--yes`, or a write blocked by `--readonly`. |
| `130` | Cancelled (Ctrl-C). |

Gate your automation on the exit code first, then parse the envelope.

---

## 6. Authentication for agents

Provide credentials by environment variables (best for CI and supervised agents) or a raw
token; no interactive login required.

```bash
export UMBRACO_HOST=https://mysite.com
export UMBRACO_CLIENT_ID=umbraco-back-office-my-api-user
export UMBRACO_CLIENT_SECRET=<secret>
umbraco content list --output json
```

Or a one-off bearer token: `umbraco content list --token <bearer> --host https://mysite.com`.

Verify the setup end to end - host resolution, TLS, credentials, auth, identity, instance
version - with a single self-check that reports each result as data and exits non-zero on any
hard failure:

```bash
umbraco auth doctor --output json
```

Run `auth doctor` as the first step of any new session; it turns "why did that 401" into a
labelled check with a remediation hint. See [getting-started.md](getting-started.md) for the
full auth story and profiles.

## 7. Running non-interactively (the rules that bite)

- **Destructive commands refuse to run without a TTY unless you pass `--yes` / `-y`.** The
  permanent `delete` commands, every `empty-recycle-bin`, and `unpublish` (which takes live
  content offline) prompt for confirmation interactively and **abort with exit `2`** when piped
  or scripted without `--yes`. The `umbraco commands` catalog marks each of these
  `"destructive": true`, so you can know in advance which need `--yes`.
- **Pipe request bodies via stdin** with `-`:
  ```bash
  cat body.json | umbraco content create --json-body -
  echo '{ "values": [] }' | umbraco content update <id> --json-body -
  ```
- **Idempotent creates:** pass `--id <guid>` on any create. Umbraco 14+ honours a
  client-supplied id, so re-running a provisioning script does not create duplicates.

---

## 8. Known limits and escape hatches

The CLI does not yet cover the whole Management API, and in a few places it reports success for
something that did not happen. This section is the **sanctioned route** for each of those today.
Everything here is temporary and tracked against
[#187](https://github.com/worm-brain/Umbraco.Cli/issues/187); the issue number is given so you
can check whether your version still needs the workaround.

### Operations that lie about succeeding

These exit `0` and print `"status":"success"`. Do not trust the envelope alone - verify the
effect, or use the workaround.

| Command | What actually happens | Escape hatch |
|---|---|---|
| `members list --group` ([#184](https://github.com/worm-brain/Umbraco.Cli/issues/184)) | Sends the group as a free-text name/email filter, so it returns `[]` | `GET /umbraco/management/api/v1/filter/member?memberGroupName=Subscribers` |
| `dictionary create --values` ([#181](https://github.com/worm-brain/Umbraco.Cli/issues/181)) | Echoes unrecognised language codes back as saved; Umbraco drops them | Use full ISO codes (`en-US`, not `en`) and confirm with `dictionary get` |
| Any `list` ([#173](https://github.com/worm-brain/Umbraco.Cli/issues/173)) | Truncates at `--take` (default 20) with no `total` or `hasMore` | Page explicitly with `--skip`/`--take`; treat a full page as "probably more" |

### Publishing

`content publish <id>` with no `--cultures` reads the document and publishes every culture it
has. Name cultures explicitly to publish a subset.

Two things about the endpoint are worth knowing if you ever call it directly. An empty
`schedule` object is not "publish now" - Umbraco answers `200` and publishes nothing. And `"*"`
is **not** a wildcard: it is the invariant culture, so on a document that varies by culture it is
rejected with `400 "Cannot publish invariant culture when the document varies by culture."`
Enumerate the cultures instead. Both cost a test round real time (#158).

`content publish-descendants` publishes a node and everything beneath it. It is not a drop-in for
`publish` on a branch: it republishes already-published descendants, and `--include-unpublished`
pushes drafts nobody has reviewed.

A change that touches **only** the template does not mark culture variants as having pending
changes, so `publish-descendants` skips them as already published. Publish those cultures
explicitly.

### Reading content back

`content get`, `content-types get`, `data-types get`, `media get` and `members get` all return a
narrow projection - core fields only. Property values, variants, templates, configuration, media
URLs, member groups and document-type properties are **not** returned
([#168](https://github.com/worm-brain/Umbraco.Cli/issues/168),
[#160](https://github.com/worm-brain/Umbraco.Cli/issues/160),
[#170](https://github.com/worm-brain/Umbraco.Cli/issues/170),
[#172](https://github.com/worm-brain/Umbraco.Cli/issues/172),
[#185](https://github.com/worm-brain/Umbraco.Cli/issues/185)).

Two escape hatches, in order of preference:

1. **`umbraco schema export`** returns verbatim `GET /document-type/{id}`, `/data-type/{id}` and
   `/template/{id}` bodies - full fidelity, no projection. This is the right way to read schema.
   It covers **only those three**: member types and media types are not in the snapshot
   ([#186](https://github.com/worm-brain/Umbraco.Cli/issues/186)), so this escape hatch does not
   reach them.
2. **A direct Management API call** for everything else, using the same credentials - see
   "Getting a token for the direct calls above" below.

### Editing content

`content update` **merges**: the body's values are matched on alias + culture + segment, its
variants on culture + segment, and anything you leave out keeps its current value. The template
is preserved. So adding one translation is one call, with no read first:

```bash
echo '{"values":[{"alias":"title","culture":"da-DK","segment":null,"value":"Hej"}],
       "variants":[{"culture":"da-DK","segment":null,"name":"Hej"}]}' \
  | umbraco content update "$ID" --json-body -
```

`--replace` opts into the destructive behaviour: the body's values and variants replace the
item's wholesale, clearing anything absent. Use it when you are writing a document you already
hold in full. The template survives `--replace` too; change it with `--template <alias|id>`.

Before alpha.7 replace was the only behaviour, and `content get` could not return the current
values, so a safe read-modify-write was impossible with the CLI alone (#178/#179). On alpha.6,
read the document from the Management API and send the complete body back.

`--dry-run` prints the body the server will receive, which is the quickest way to confirm a merge
did what you expected.

See [commands.md](commands.md#property-value-formats-for---json-body) for the value shape each
property editor expects.

### Authoring schema

`content-types create`, `data-types create/update` and `member-types create/update` take a small
set of scalar flags. They cannot set properties, groups, configuration, templates, allowed
children, compositions or culture variance
([#161](https://github.com/worm-brain/Umbraco.Cli/issues/161),
[#169](https://github.com/worm-brain/Umbraco.Cli/issues/169)).

The sanctioned route is the snapshot round-trip:

```bash
umbraco schema export --out schema.json     # full-fidelity bodies
# edit .documentTypes[] / .dataTypes[] - generate GUIDs for new containers and properties
umbraco schema apply schema.json --dry-run  # preview the plan
umbraco schema apply schema.json
```

Not yet covered by the snapshot, and needing a direct Management API call: **member types with
properties** and **media types** ([#186](https://github.com/worm-brain/Umbraco.Cli/issues/186)),
**media folders** ([#171](https://github.com/worm-brain/Umbraco.Cli/issues/171)) and **domains /
Culture and Hostnames** ([#180](https://github.com/worm-brain/Umbraco.Cli/issues/180)). Domains
in particular are required for a multilingual site: without them Umbraco logs "The root node was
published with multiple cultures, but no domains are configured" and the non-default culture is
unreachable.

```bash
# route /da/ to the Danish variant
curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "$UMBRACO_HOST/umbraco/management/api/v1/document/$ID/domains" \
  -d '{"defaultIsoCode":"en-US","domains":[{"domainName":"example.com","isoCode":"en-US"},{"domainName":"example.com/da","isoCode":"da-DK"}]}'
```

### Getting a token for the direct calls above

Several escape hatches here are raw Management API calls. They use the **same API user** the CLI
is configured with, so no extra setup is needed - exchange the client credentials for a bearer
token at the same endpoint the CLI uses:

```bash
TOKEN=$(curl -s -X POST "$UMBRACO_HOST/umbraco/management/api/v1/security/back-office/token" \
  -d grant_type=client_credentials \
  -d client_id="$UMBRACO_CLIENT_ID" \
  -d client_secret="$UMBRACO_CLIENT_SECRET" | jq -r .access_token)

curl -s -H "Authorization: Bearer $TOKEN" \
  "$UMBRACO_HOST/umbraco/management/api/v1/document/$ID"
```

```powershell
$body = @{ grant_type = 'client_credentials'; client_id = $env:UMBRACO_CLIENT_ID; client_secret = $env:UMBRACO_CLIENT_SECRET }
$token = (Invoke-RestMethod -Method Post -Uri "$env:UMBRACO_HOST/umbraco/management/api/v1/security/back-office/token" -Body $body).access_token
Invoke-RestMethod -Uri "$env:UMBRACO_HOST/umbraco/management/api/v1/document/$ID" -Headers @{ Authorization = "Bearer $token" }
```

Tokens are short-lived; fetch one per script run rather than storing it. Note that a direct call
bypasses every guardrail in section 9 - `--readonly` and the allow-list constrain the CLI, not
`curl`. If you are the supervising process, that is the reason to prefer a CLI command once one
exists.

### Parse errors are not JSON

An invalid argument (a non-GUID where a GUID is expected, a missing required option) is reported
as **plain text on stderr plus help text on stdout**, ignoring `--output json`
([#167](https://github.com/worm-brain/Umbraco.Cli/issues/167)). Piping such a run into `jq`
fails with a parse error rather than yielding an error envelope. Check the exit code before
parsing: `1` can mean either an API error (envelope on stderr) or a parse error (no envelope).

## 9. Guardrails (for whoever supervises the agent)

Two mechanisms constrain what a session can do. They are most useful set by the **parent
process** that supervises an agent, where the agent itself cannot change them.

### Read-only mode

`--readonly` (or `UMBRACO_READONLY=1`) refuses every write (create/update/delete/publish) with
a clear error and a non-zero exit; reads are unaffected.

### Command allow-list

`UMBRACO_ALLOWED_COMMANDS` (or the config `allowedCommands` field) restricts which commands may
run. Entries are noun groups (`content`, `media`) and/or full command names (`content.list`); a
command runs only if its group or full name is listed. The `auth` group is always allowed. A
blocked command aborts before running with exit `2`.

- **Unset vs lockdown:** only a *truly unset* value (variable absent and no config
  `allowedCommands`) means no restriction. Any *present* value that is blank or separators-only
  (`" "`, `","`) is an explicit lockdown - nothing runs but the always-allowed `auth` group.
- **Tighten-only:** the effective list is the most restrictive of the default store's and the
  resolved one, so switching `--profile` or pointing `--config` at another file can only ever
  *tighten* access, never widen it.

```bash
# An agent that may only read content and media, and never write:
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media umbraco content list
```

**Enforcement boundary:** the environment-variable forms are the real boundary - set them in
the supervising process. The config-file `allowedCommands` form is a convenience default.

### Preview writes

`--dry-run` on any write prints the request it would send (method, URL, body) and exits `0`
without changing anything (`"status": "dry-run"`). Use it to show a plan before committing.

---

## 10. Recipes

### Provision idempotently

Create with a fixed `--id` so re-runs converge instead of duplicating:

```bash
umbraco content-types create --name "Blog Post" --alias blogPost
umbraco content create --content-type blogPost --name "Hello" --id 3f2a...  # same id each run
```

### Move schema between environments

Export document types, data types, and templates to a portable snapshot, diff it against a
target, then apply. See [commands.md](commands.md#schema-export--diff--apply).

```bash
umbraco schema export --out schema.json                 # from source
umbraco schema diff schema.json                         # against target (read-only)
umbraco schema apply schema.json --dry-run              # preview the whole plan
umbraco schema apply schema.json                        # create + update (never deletes)
umbraco schema apply schema.json --prune --yes          # also delete what the snapshot omits
```

### Move or sync content between environments

The content pipeline mirrors schema. See [commands.md](commands.md#content-export--diff--apply).

```bash
umbraco content export --root <id> --out content.json
umbraco content diff content.json
umbraco content apply content.json --dry-run
```

### Bulk operations from a query

Bulk commands read ids one per line from `--file` or stdin, and report each id independently in
the `data` results array (`{id, status, error}`); the exit code is `1` if any item failed.

```bash
umbraco content list --fields id | jq -r '.data[].id' | umbraco content bulk publish
```

On Windows PowerShell (no `jq`), use the built-in `ConvertFrom-Json`:

```powershell
(umbraco content list --fields id --output json | ConvertFrom-Json).data.id | umbraco content bulk publish
```

### A read-only audit agent

```bash
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media,server,health,log-viewer \
  umbraco auth doctor && umbraco server info && umbraco health list
```

---

## 11. A note on "AI-focused" docs

These docs are plain Markdown on purpose. Claude Code auto-loads `CLAUDE.md` (which redirects
to `AGENTS.md`); other coding agents look for `AGENTS.md`; agents without a special convention
just read Markdown. There is no proprietary ingestion format to satisfy here - stable headings,
explicit contracts, and copy-pasteable non-interactive examples are what make a page usable by
any of them. The most machine-friendly source of truth is not prose at all: it is
`umbraco commands` and `--schema`.
