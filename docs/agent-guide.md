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

Each option and argument also says what it defaults to (`default`, when it has one) and, when it
is required only in some modes, which options make it unnecessary (`requiredUnless`, e.g.
`content create --name` has `["--json-body", "--schema", "--example"]` and reports `required: false`). A
command that takes `--json-body` names the command that prints the body's schema
(`jsonBodySchema: "umbraco content create --schema"`).

```bash
umbraco commands | jq '.. | objects | select(.requiredUnless) | {name, requiredUnless}'
```

Prefer this over hard-coding command knowledge. When you diff the catalog across CLI versions,
compare `.data` only - `meta.timestamp` changes on every call.

## 2. Discover request bodies: `--schema`

Any command that accepts a `--json-body` also accepts `--schema`, and it means the same thing
everywhere: print the JSON Schema of that body and exit. It is local (no host/auth), it ignores
`--output` (always a bare JSON Schema document, not the envelope), and it is never blocked by the
allow-list or `--readonly`. For the schema nouns (`document-type`, `media-type`, `member-type`,
`data-type`, `template`) the schema is generated from the Management API spec; an `update`
schema requires no key, because the body is merged.

Those nouns also take `--example`, which prints a **real** item from the instance (or a minimal
valid body on a site with none) - usually the best starting point for a body you will edit. It
needs a host.

For content, `content create --example --document-type <alias>` prints a create body for that
type with one `values[]` entry per property (compositions included), each holding an example
of the value shape its editor takes, plus the `editorAlias` it was chosen by. An editor it does
not know gets `"value": null` - look that one up rather than guessing. Ids you need not choose
(a media picker entry's `key`) are already fresh GUIDs; replace the remaining `<...>`
placeholders (`<media id>`, `<document id>`), then pass the file to
`content create --json-body`, which ignores `editorAlias`.

```bash
umbraco content create --schema          # the shape of a content-create body
umbraco document-type update --schema    # the shape of a document-type update body
umbraco document-type create --example   # a real document type to start from
```

Feed the schema straight to a validator, or use it to construct a valid body before sending.

---

## 3. The output contract

Every successful command emits this envelope on **stdout**:

```json
{
  "status": "success",
  "data": { },
  "meta": { "command": "content.list", "durationMs": 142, "schemaVersion": "6" }
}
```

A **list** result's `data` is an array of the same objects the matching `get` returns - same field
names, same types - and its `meta` says how much more there is:

```json
{
  "status": "success",
  "data": [ { "id": "...", "name": "Home", "isPublished": true } ],
  "meta": {
    "command": "content.list", "durationMs": 142, "schemaVersion": "6",
    "total": 237, "skip": 0, "take": 100, "hasMore": true
  }
}
```

Every paged `list` defaults to `--take 100`. To get everything, pass `--all`: the CLI pages until
the collection is exhausted and reports `hasMore: false`, or fails with `invalid_argument` past
10,000 items rather than truncating. `--all` cannot be combined with `--skip`/`--take`.
`total`, `skip`, `take` and `hasMore` are
**omitted when the source cannot report them** - an
absent `hasMore` means "unknown", not "no". Never read a missing `total` as a complete list. Many
commands (`content tree`, `content find --path`, `manifest list`) genuinely cannot count, and say
so by omission rather than claiming completeness.

`--output csv` cannot carry `meta`, so a truncated CSV prints the same "showing N of M" line to
stderr that human output does - stdout stays loadable as-is.

**Every write returns `data` too.** `create`, `update`, `copy` and `upload` return the resulting
item, as `get` shows it. Every other write - delete, move, trash, restore, publish, sort, and the
like - returns `{ "id": ... }` (or `{ "ids": [...] }`) of what it acted on; one with no target
returns the state it left (`redirect tracking enable` returns the tracking status), or `{}` when
there is nothing to read back (`empty-recycle-bin`). `content publish` returns
`{ id, published, publishAt, unpublishAt, cultures }`, so a scheduled publish reads as
`"published": false`. The confirmation text ("Deleted.") is human output only.

Errors go to **stderr**:

```json
{
  "status": "error",
  "exitCode": 1,
  "httpStatus": 404,
  "message": "Content item not found",
  "category": "request_rejected",
  "meta": { "command": "content.get", "timestamp": "2026-09-25T20:00:00Z", "schemaVersion": "6" }
}
```

`exitCode` is the process exit code; `httpStatus` is the server's status and is **absent when the
request never reached the server** (a policy refusal, an unreachable host, a timeout). They were
a single `code` field before schemaVersion 3, which meant you could not act on it without already
knowing which kind of failure you had.

**Every error carries a `category`**, so you can tell **whose** problem it is without parsing the
message; an error from an Umbraco API call also carries the `serverVersion` when the server
responded:

```json
{
  "status": "error",
  "exitCode": 1,
  "httpStatus": 500,
  "message": "The Umbraco server returned an internal error (HTTP 500). This is a server-side problem, not a rejected request; check the Umbraco logs.",
  "category": "server_error",
  "serverVersion": "17.3.5",
  "meta": { "command": "content.get", "timestamp": "2026-09-25T20:00:00Z", "schemaVersion": "6" }
}
```

When Umbraco explains the failure, its answer is passed through (#286). `message` is built from
it (what failed, Umbraco's `operationStatus`, the reason, and which properties to fix), and
`details` carries the body exactly as Umbraco sent it (its ProblemDetails, including any stack
trace in `details.detail`):

```json
{
  "status": "error",
  "exitCode": 1,
  "httpStatus": 400,
  "message": "Invalid document (ContentInvalid): The specified document had an invalid configuration. Invalid properties: author, publishDate, excerpt, bodyText.",
  "category": "request_rejected",
  "details": {
    "title": "Invalid document",
    "detail": "The specified document had an invalid configuration.",
    "operationStatus": "ContentInvalid",
    "invalidProperties": ["author", "publishDate", "excerpt", "bodyText"]
  },
  "serverVersion": "17.7.0",
  "meta": { "command": "content.publish", "timestamp": "2026-09-28T09:00:00Z", "schemaVersion": "6" }
}
```

Read `details.operationStatus` to branch on Umbraco's reason (`ContentInvalid`, `CannotInvite`,
`DuplicateAlias`...) and `details.invalidProperties` / `details.errors` for what to fix. `details`
is absent when there was no body to pass on (the host was unreachable, or the CLI refused before
calling Umbraco).

For an API call, `category` is one of: `unreachable` (no response - DNS/connection), `timeout`,
`request_rejected` (a 4xx - usually bad input or the request itself), `server_error` (a 5xx or an
undeclared status - a server-side fault) or `unexpected_response` (the body did not match what the
CLI expected, a likely version mismatch). `serverVersion` is omitted when the server could not be
reached (`unreachable`/`timeout`) or the version could not be determined.

The rest never reach the API, so they carry no `httpStatus` and no `serverVersion`:

| `category` | Exit | Meaning |
|---|---|---|
| `invalid_argument` | 1 | Your input: a command line that does not parse, a malformed or contradictory `--json-body`, two inputs for one value, an alias or name that matches nothing (or several items - the message lists their ids), a value the instance does not recognise (a webhook `--event` alias, a dictionary ISO code), a file that is not there. |
| `internal` | 1 | An unexpected error inside the CLI - a bug to report. |
| `not_authenticated` | 2 | No host, no credentials, an unknown `--profile`, or authentication failed. |
| `not_allowed` | 2 | The command is not in the allow-list. |
| `readonly` | 2 | A write blocked by `--readonly` / `UMBRACO_READONLY`. |
| `confirmation_required` | 2 | A destructive command run non-interactively without `--yes`. |
| `refused` | 2 | A pre-flight check refused: a delete that would take or orphan something else (an in-use type, data type or template, a group with members or users, a dictionary item with children, any language) without `--force`. |
| `cancelled` | 2 | You declined the confirmation prompt. |

A write command run with `--dry-run` uses a distinct status and does not touch the server:

```json
{ "status": "dry-run", "data": { "method": "POST", "url": ".../webhook", "body": { } },
  "meta": { "command": "webhook.create", "durationMs": 12, "timestamp": "...", "schemaVersion": "6" } }
```

The payload is under `data`, like every other success envelope - it was `request` before
schemaVersion 3, the one exception to that rule.

To see what was actually sent and received when a call fails, add `-v`: every request and
response is logged to **stderr** (stdout stays parseable) with its body - the response cut at
4 KB - and credentials redacted.

### Contract stability rules

- The field names above (`status`, `data`, `meta`, `command`, `durationMs`, `schemaVersion`,
  the error `exitCode`/`httpStatus`/`message`, and the error `category`/`serverVersion`) are part
  of the contract and are never renamed silently.
- `meta.schemaVersion` (currently `"6"`) is bumped **only** on a breaking change - a renamed or
  removed field, or a changed meaning. New fields can appear without a bump.
- Therefore: **ignore unknown fields**, and if you want to be defensive, gate on
  `meta.schemaVersion`.
- **`list` results carry the same objects as the corresponding `get`** - same field names, same
  types. A boolean is a boolean, not `"True"`. This holds by construction: list output is
  serialized from the same objects `get` returns, rather than from the human table.
- An absent `meta` field means **unknown**, never a default. A list with no `total` is one whose
  source could not count, not one that is complete.

**What changed in schemaVersion 6**, if you are moving from `"5"`: Umbraco's error details, the
`auth` profile commands, what creates return, and the content read model.

| Before | Now |
|---|---|
| an error's `message` was ProblemDetails `detail` (a stack trace on some 500s), or "check the Umbraco logs" when the body was never read | built from `title`, `operationStatus`, the first line of `detail`, and `invalidProperties` (#286) |
| no structured error body | `details`: Umbraco's ProblemDetails, as sent |
| `auth profile list` `"default": "*"` or `""` | `"default": true` / `false` |
| `auth logout` `{"removed": true}` (no `profile` unless `--profile` was given) | `{"profile": "<name>", "removed": true, "defaultCleared": false}` - always the profile it acted on |
| `auth login` `profile` missing without `--profile` | always the profile the credentials were saved to |
| `document-type` / `media-type` / `member-type` / `data-type create --json-body` `{id, name, alias}`; the flag-only creates echoed the flags (`properties: []`) | the saved item read back, exactly as `get` prints it (#285) |
| `webhook create` `events: [{"eventName": "Umbraco.ContentPublish"}]` (the alias) | `events: [{"eventName": "Content Published", "eventType": "Content", "alias": "Umbraco.ContentPublish"}]`, as `webhook list` (#295) |
| `stylesheet` / `script` / `partial-view create` `path: "blocklist/site.css"` | `path: "/blocklist/site.css"`, as `list` and `get` (#296); `--parent` accepts `/blocklist/` too |
| `content get` / `list` / `find` `"contentType": {id, alias}` | `"documentType": {id, alias, icon, collection?}` - the key the create body, `--document-type` and `content version get` use (#284). `content create --json-body` still reads either key |
| `content get` without `isTrashed`, `flags`, `urls`; variants without `id`, `flags`; `scheduledPublishDate` / `scheduledUnpublishDate` always null | every field of `GET /document/{id}` (#306, #297), plus `urls: [{culture, url}]` (#289). List rows carry `isTrashed` and `flags` too |
| `dictionary get` / `create` / `update` / `list` without `parent` | `parent: {id}` (null at the root, #290) |
| `document-blueprint get` / `create` with no top-level `name` or `parent` | `name` (the first variant's, as `content get`) and `parent: {id}` when it is in a folder (#298) |
| `content diff` rows `{change, id, parent, changes}` | `{change, id, name, documentType, parent, changes}`: the document's name (invariant or default-language variant) and its document type alias, null when unknown (#293) |
| `content apply` rows `{operation, id, status, cultures}` | `{operation, id, status, name, documentType, cultures}`, on every step including deletes and `--dry-run` (#293) |

Behaviour that changed with it: `auth login` and `auth logout` resolve the profile like every
other command (`--profile`, else `UMBRACO_PROFILE`, else the default); before, both ignored
`UMBRACO_PROFILE` and acted on the default (#301, #303). Logging out of the default profile no
longer promotes another profile to be the default: `defaultCleared` is `true`, and commands
without a profile fail until `auth profile use <name>` picks one (#304). The global `--fields`
and `--quiet` now apply under `auth` too (#305). `content diff` no longer reports `Umbraco.Label`
values, which Umbraco never accepts from a write, and `content apply` warns on stderr once per
Label property it could not promote (#291). `document-type` / `media-type delete` (and
`schema apply --prune`) count the items of the type and refuse only when there are some, or when
the type is a composition or an element type; before, they always needed `--force` (#287).
`relation list --relation-type` and `relation-type get` take the alias (#300).

**What changed in schemaVersion 5**, if you are moving from `"4"`: the **diff and apply**
reports (`content diff`, `schema diff`, `content apply`, `schema apply`), a member's `groups`, and
the #268 surface batch:

- `meta.command` uses the renamed nouns and verbs (`document-type.list`, `content.version.list`).
- Every error has a `category` and a full `meta`; CSV errors gain a `category` column.
- The message-only success (`{status, message, meta}`) is gone: those writes emit `data`.
- The dry-run envelope carries the full `meta`.

| Before | Now |
|---|---|
| `"idMismatch": "yes"` or `""` | `"idMismatch": true` / `false` |
| `"parent": ""`, `"currentId": ""`, `"note": ""` | `null` |
| no way to tell what a `Changed` row changed | `"changes": ["values.title[en-US]", "state[da-DK]"]` (null for added/removed rows) |
| no `meta.total` | `meta.total`, `skip: 0`, `hasMore: false` (a diff is complete) |
| `content apply` rows `{operation, id, status}` | also `cultures` (for a publish/unpublish of a variant document), and the operations `publish`/`unpublish` |
| `member get`/`list` `"groups": ["<id>"]` | `"groups": [{"id": "<id>", "name": "Subscribers"}]`, and `memberType.alias` is filled |

**What changed in schemaVersion 4**, if you are moving from `"3"`: only the **bulk** envelope.

| Before | Now |
|---|---|
| bulk `status` always `"success"`, even when every item failed | `"success"`, `"partial"` (some failed), `"error"` (all failed) or `"dry-run"` |
| no counts | `meta.summary`: `{succeeded, failed, dryRun}` |
| a bulk `--dry-run` item was `{id, status}` | it also carries `request`: `{method, url, body}` |

**What changed in schemaVersion 3**, if you are moving from `"2"`:

| Before | Now |
|---|---|
| `"published": "True"` (string, from the table caption) | `"isPublished": true` |
| `"default"` / `"mandatory"` on `language list` | `"isDefault"` / `"isMandatory"` |
| every list value was a string | booleans, numbers and dates keep their types |
| `"code": 404` (exit code *or* HTTP status) | `"exitCode": 1` **and** `"httpStatus": 404` |
| error `schemaVersion` at the top level, no `meta` | error carries `meta`, like every other envelope |
| `--dry-run` payload under `request` | under `data` |
| lists had no paging information | `meta.total` / `skip` / `take` / `hasMore` |

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
  as an `exitCode,httpStatus,category,message` row.
- **`--quiet` / `-q`** drops the result of writes (the confirmation and its `data`) but still
  emits reads, errors, and exit codes.

## 5. Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including a `--dry-run` preview). |
| `1` | The command ran and failed: an API error, invalid input (`invalid_argument`), a bulk run where some items failed, or an unexpected error (`internal`). |
| `2` | Aborted before running: not authenticated, blocked by the allow-list or `--readonly`, confirmation missing or declined, or refused by a pre-flight check. |
| `130` | Cancelled (Ctrl-C). |

Since schemaVersion 3 the JSON error envelope reports this as `exitCode`, separately from the
server's `httpStatus` - so `exitCode: 1` with `httpStatus: 404` is an API 404, while `exitCode: 1`
with no `httpStatus` never reached the server at all. A parse error is also reported this way
rather than as plain text plus a help screen (#167), so `-o json` stays parseable when you mistype.

Gate your automation on the exit code first, then read the envelope's `category` for why.

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
  permanent `delete` commands, every `empty-recycle-bin`, `unpublish` (which takes live content
  offline) and `redirect tracking disable` prompt for confirmation interactively and **abort with
  exit `2`** (`confirmation_required`) when piped or scripted without `--yes`. Cost alone does not
  gate: `indexer rebuild` and `models-builder build` lose nothing and run without it.
  The `umbraco commands` catalog marks each of these `"destructive": true`, read from the same
  declaration the gate uses, so it cannot disagree with what actually asks. `content apply` and
  `schema apply` are destructive only with `--prune`, and every `update` (and
  `content domain set`) only with `--replace`; the catalog says so with `"destructiveWhen"`.
  Reversible writes - `move`, `copy`, `publish`, `redirect tracking enable` - never need `--yes`.
- **Deleting a type that content uses needs `--force` as well as `--yes`.** `data-type delete`
  (while in use), `member-type delete` (while it has members), and `document-type` /
  `media-type delete` (while any item of the type exists, counting the recycle bin, while another
  type uses it as a composition, or for an element type, whose block usage Umbraco does not
  report) are refused with exit `2` unless
  `--force` is given - checked **before** any confirmation prompt, and under `--dry-run` too,
  since a refusal is what a real run would do. `schema apply --prune` applies the same check to
  every type it would delete; its `--dry-run` plan marks those steps `needs --force`.
- **Pipe request bodies via stdin** with `-`:
  ```bash
  cat body.json | umbraco content create --json-body -
  echo '{"values":[{"alias":"title","culture":"en-US","value":"Hello"}]}' \
    | umbraco content update <id> --json-body -
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

### Publishing

`content publish <id>` with no `--culture` reads the document and publishes every culture it
has. Name cultures explicitly to publish a subset. `content unpublish` (and `bulk unpublish`)
works the same way: no `--culture` unpublishes every culture of a variant document, and the
whole of an invariant one.

Other commands pick a sensible culture when you name none. `content create` and
`document-blueprint create` use the default language when the document type varies by culture.
In `content update` and `document-blueprint update`, a variant with no `culture` renames the
default-language variant. It is refused if the item has no variant in that language. Values with
no `culture` are left as sent. `content version list` lists every culture's history, newest first, and tags each row with the
`culture` to pass to `content version rollback --culture`. Use `content version get <version-id>` to read a
version's values before rolling back. `document-blueprint create --from-document --name` renames every
culture.

A rollback only changes the draft, and `content restore` brings an item back unpublished and
last in its parent's sort order, so neither changes the live site. Add `--publish` to either
to publish afterwards. In `content version list`, choose the version by its
`isCurrentDraftVersion` / `isCurrentPublishedVersion` flags rather than by date, because the
current draft and the current published version can share a `versionDate`.

`content restore <id>` puts the item back under the parent it was trashed from. Pass
`--parent <id>` to choose another parent, or `--to-root` for the content root.

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

The by-id reads now return what they fetch, so a `get -> edit -> update` round-trip works through
the CLI alone:

| Command | Returns |
|---|---|
| `content get` | every field of `GET /document/{id}` under the API's keys - `documentType`, `values` (with each value's `editorAlias`), every `variant` with its `state`, `id`, `flags` and `scheduledPublishDate` / `scheduledUnpublishDate`, `template`, `isTrashed`, `flags` - plus `name`, `parent` and `urls` per culture |
| `media get` | `values` carrying `umbracoWidth`/`umbracoHeight`/`umbracoBytes`/`umbracoExtension`, plus `urls` per culture |
| `document-type`, `media-type`, `member-type`, `data-type`, `template` `get` | the Management API body verbatim - `properties`, `containers`, `compositions`, `allowedTemplates`, `collection`, allowed children, per-property `validation`; a data type's `values`; a template's `content` - which is a valid `update --json-body` as it stands |

`member get` returns the member's `groups` and `values` too
([#185](https://github.com/worm-brain/Umbraco.Cli/issues/185)). `content get` and `media get`
carry `parent` (read from the tree, because the by-id body has none; left out at the root), as do
`dictionary get` and `document-blueprint get`, and `data-type list` rows carry their folder.
`content list` rows leave out `updateDate`, `values`, `urls` and the full `variants`: the tree
endpoint behind them does not return those, so read one item with `content get` when you need
them, rather than trusting a default date.

Type references (`documentType`, `mediaType`) carry a resolved `alias`
([#163](https://github.com/worm-brain/Umbraco.Cli/issues/163)). When it cannot be resolved the
field is **omitted** rather than returned as an empty string, so treat its absence as "unknown"
rather than "no alias".

Two escape hatches remain useful:

1. **`umbraco schema export`** returns the verbatim get-by-id body of every document type,
   media type, member type, data type and template - full fidelity, no projection - plus every
   language, dictionary item, and member and user group, and the partial views, stylesheets and
   scripts with their content
   ([#186](https://github.com/worm-brain/Umbraco.Cli/issues/186),
   [#227](https://github.com/worm-brain/Umbraco.Cli/issues/227),
   [#292](https://github.com/worm-brain/Umbraco.Cli/issues/292)). This is the right way to read
   schema.
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

**Merging ships in 0.1.0-alpha.11.** On **0.1.0-alpha.10 and earlier**
replace is the only behaviour, the template is cleared on every update, and `content get` cannot
return the current values - so a safe read-modify-write is impossible with the CLI alone
(#178/#179). On those versions, read the document from the Management API, send the complete body
back, then re-set the template and republish.

`--dry-run` prints the body the server will receive, which is the quickest way to confirm a merge
did what you expected.

See [commands.md](commands.md#property-value-formats-for---json-body) for the value shape each
property editor expects.

### Authoring schema

The scalar flags on `document-type create`, `data-type create/update` and
`member-type create/update` cannot express properties, groups, editor configuration, templates,
compositions or culture variance. **Pass the Management API body instead**
([#161](https://github.com/worm-brain/Umbraco.Cli/issues/161),
[#169](https://github.com/worm-brain/Umbraco.Cli/issues/169)):

```bash
umbraco document-type get blogPost -o json | jq .data > t.json
# ...edit t.json: add a property, a group, a template...
umbraco document-type update blogPost --json-body t.json
```

`--json-body` is on `create` and `update` for `document-type`, `media-type`, `member-type` and
`data-type`, and on `template update`, and takes a file or `-` for stdin. `update` **merges the
body's top-level keys** into the item, so a partial body is safe: a key you leave out keeps its
value, a key you send replaces it whole. Pass `--replace` (with `--yes`) to send the body as the
whole item. `--schema` prints the body's JSON Schema, generated from the Management API spec, and
`--example` prints a real item off the instance to start from (it needs a host; on a site that has
none yet, it prints a minimal valid body). Both `update` and `create` return the resulting item.

A blueprint's scaffold is a valid content body: `document-blueprint scaffold <id> | umbraco
content create --json-body -` creates a new item from it (its `documentType` is read as the
`contentType`). The scaffold carries no `id`, so each create makes a new item; a body that does
carry an `id` keeps it, in either shape (#299).

The snapshot round-trip is still the right tool for a **set** of types, or for moving schema
between environments:

```bash
umbraco schema export --out schema.json     # full-fidelity bodies
# edit .documentTypes[] / .dataTypes[] - generate GUIDs for new containers and properties
umbraco schema apply schema.json --dry-run  # preview the plan
umbraco schema apply schema.json
```

**Templates travel with their partials.** The snapshot (format `"4"`) carries `partialViews`,
`stylesheets` and `scripts` as `{path, content}` per file and `{path, isFolder: true}` per folder,
so a promoted template no longer 500s for want of its partial
([#292](https://github.com/worm-brain/Umbraco.Cli/issues/292)). Apply creates the folders and
files before the templates, and writes each file byte for byte. If your views deploy from git,
export with `--no-files`: a snapshot **without** those sections (including every format `"3"`
file) does not manage files at all, so diff and apply leave the target's files alone and
`--prune` never deletes one. A section that is present but empty **does** manage them: `--prune`
then deletes every live file of that kind. `--prune` refuses to delete a file a template names
(`Html.PartialAsync("header")` names `/header.cshtml`) unless you add `--force`. A diff row
noted `line endings only` differs only in CRLF/LF or a trailing newline.

Folders for these files are CLI verbs too:
`umbraco partial-view folder create --name Components --parent blocklist` (and `delete`, and the
same for `stylesheet` and `script`, [#238](https://github.com/worm-brain/Umbraco.Cli/issues/238)).

**Media folders** and **domains** are CLI verbs now, not direct API calls:

```bash
umbraco media folder create --name Blog                     # id goes to media upload --parent

# route /da/ to the Danish variant. Without domains, Umbraco logs "the root node was published
# with multiple cultures, but no domains are configured" and serves nothing but the default.
umbraco content domain set "$ID" --default en-US \
  --domain example.com=en-US --domain example.com/da=da-DK
```

Member types with properties and media types are in the snapshot too
([#186](https://github.com/worm-brain/Umbraco.Cli/issues/186)), so the round-trip above reaches them
as well. So are languages, the dictionary, and member and user groups
([#227](https://github.com/worm-brain/Umbraco.Cli/issues/227)): a promotion no longer needs
`language create`, a dictionary script or `member-group create --id` before `content apply`.
User groups travel without their start nodes and per-document permissions, and apply keeps the
target's own. Domains stay per environment (`content domain set`). Note the snapshot format is
now **version 3**: a file exported by an older CLI is refused, because reading it would look like
"this instance should have none of the newer kinds" and `apply --prune` would act on that.
Re-export.

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

### Parse errors are envelopes too

An invalid argument is reported like any other error: an envelope with `category`
`invalid_argument` and a message that says what was expected, e.g. `'Blog' is not valid for
--parent: expected a GUID id.` ([#167](https://github.com/worm-brain/Umbraco.Cli/issues/167),
#211).

## 9. Guardrails (for whoever supervises the agent)

Two mechanisms constrain what a session can do. They are most useful set by the **parent
process** that supervises an agent, where the agent itself cannot change them.

### Read-only mode

`--readonly` (or `UMBRACO_READONLY=1`) refuses every write - any non-GET request, so `sort`,
`health run` and the like as well as create/update/delete/publish - with exit `2` and category
`readonly`; reads are unaffected. Every write is marked `"mutating": true` in `umbraco commands`.

### Command allow-list

`UMBRACO_ALLOWED_COMMANDS` (or the config `allowedCommands` field) restricts which commands may
run. Entries are noun groups (`content`, `media`) and/or full command names (`content.list`); a
command runs only if its group or full name is listed. The `auth` group is always allowed. A
blocked command aborts before running with exit `2` and category `not_allowed`.

- **Renamed nouns still match:** an entry written before the #268 renames
  (`UMBRACO_ALLOWED_COMMANDS=content-types`, `content.domains.set`) allows exactly what it allowed
  before, under the new names (`document-type`, `content.domain.set`) - and nothing more.

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
umbraco document-type create --name "Blog Post" --alias blogPost
umbraco content create --document-type blogPost --name "Hello" --id 3f2a...  # same id each run
```

### Move schema between environments

Export every schema entity - document types, media types, member types, data types,
templates, languages, dictionary items, member and user groups, and the partial views,
stylesheets and scripts the templates render - to a portable snapshot, diff it against a target,
then apply. See [commands.md](commands.md#schema-export--diff--apply).

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

Content references media by id, so promote the media first with the media pipeline, which keeps
every item's GUID and carries its file ([commands.md](commands.md#media-export--diff--apply)). The
order is schema, then media, then content:

```bash
umbraco schema export --out schema.json && umbraco media export --out ./media && umbraco content export --out content.json   # source
umbraco schema apply schema.json && umbraco media apply ./media && umbraco content apply content.json                       # target
```

A media snapshot is a directory (`media.json` plus `files/`), so it cannot be piped.

### Bulk operations from a query

Bulk commands read ids from `--file` or stdin, and report each id independently in
the `data` results array (`{id, status, error}`); the exit code is `1` if any item failed. The
envelope's `status` says how the batch went - `success`, `partial`, `error` (every item failed)
or `dry-run` - with the counts in `meta.summary`, and it is written to stdout in every case.
Under `--dry-run` each item carries the `request` it would have sent.

The input can be the CLI's own output, so a list pipes straight in with no `jq` (#288):

```bash
umbraco content list --fields id | umbraco content bulk publish
```

The format is read from the content. JSON (first character `{` or `[`) is the success envelope,
whose `data` items each give their `id`, an array of objects with `id`, or an array of id strings.
CSV is recognised by a header row with an `id` column (`-o csv`), and that column is used. Anything
else is one id per line. JSON items without an `id`, or a CSV row with an empty `id`, refuse the
whole batch as `invalid_argument` (exit 1) before anything runs; an id that is not a GUID is still
reported per item.

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
