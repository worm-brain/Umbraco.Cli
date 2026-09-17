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

A write command run with `--dry-run` uses a distinct status and does not touch the server:

```json
{ "status": "dry-run", "request": { "method": "POST", "url": ".../webhook", "body": { } } }
```

### Contract stability rules

- The field names above (`status`, `data`, `meta`, `command`, `durationMs`, `schemaVersion`,
  and the error `code`/`message`) are part of the contract and are never renamed silently.
- `meta.schemaVersion` (currently `"2"`) is bumped **only** on a breaking change - a renamed or
  removed field, or a changed meaning. New fields can appear without a bump.
- Therefore: **ignore unknown fields**, and if you want to be defensive, gate on
  `meta.schemaVersion`.
- `list` results use the same camelCase keys as the corresponding `get`, so a field has the
  same name wherever it appears (this is what schemaVersion 2 established).

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

## 8. Guardrails (for whoever supervises the agent)

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

## 9. Recipes

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
umbraco content list --fields id | jq -r '.[].id' | umbraco content bulk publish
```

### A read-only audit agent

```bash
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media,server,health,log-viewer \
  umbraco auth doctor && umbraco server info && umbraco health list
```

---

## 10. A note on "AI-focused" docs

These docs are plain Markdown on purpose. Claude Code auto-loads `CLAUDE.md` (which redirects
to `AGENTS.md`); other coding agents look for `AGENTS.md`; agents without a special convention
just read Markdown. There is no proprietary ingestion format to satisfy here - stable headings,
explicit contracts, and copy-pasteable non-interactive examples are what make a page usable by
any of them. The most machine-friendly source of truth is not prose at all: it is
`umbraco commands` and `--schema`.
