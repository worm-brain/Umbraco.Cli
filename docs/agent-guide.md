# Automation guide

This guide is for scripts, CI jobs and AI agents that drive the `umbraco` CLI. Setting the tool up
yourself? Start with [getting-started.md](getting-started.md). Looking for one command? See
[commands.md](commands.md).

Two things make the CLI easy to drive from code:

1. **Everything is JSON.** When stdout isn't a terminal, every command prints the same envelope.
2. **The CLI describes itself.** You can list every command, and the exact shape of every request
   body, without a server and without scraping `--help`.

Read the two discovery sections first. With those, you can work most things out yourself.

---

## 1. Discover the surface: `umbraco commands`

`umbraco commands` prints the whole command tree as JSON in one call: every command, its
arguments, options, value types, which ones are required, one-line help, and a `destructive` flag
on commands that need care. It's a **local** command - no host, no authentication.

```bash
umbraco commands | jq '.data.commands[].name'         # top-level nouns
umbraco commands | jq '.. | .name? // empty'          # every command name
```

Each option and argument also gives its `default` (when it has one) and, when it's only required
in some modes, which options make it unnecessary (`requiredUnless`). For example,
`content create --name` has `["--json-body", "--schema", "--example"]` and reports
`required: false`. A command that takes `--json-body` names the command that prints the body's
schema (`jsonBodySchema: "umbraco content create --schema"`). Help examples are listed on their
own too (`examples`, one command line each), so you don't have to cut them out of `description`.

```bash
umbraco commands | jq '.. | objects | select(.requiredUnless) | {name, requiredUnless}'
```

Use this rather than hard-coding what you know about commands. When you diff the catalog across
CLI versions, compare `.data` only - `meta.timestamp` changes on every call.

## 2. Discover request bodies: `--schema`

Any command that takes `--json-body` also takes `--schema`, and it means the same thing
everywhere: print the JSON Schema of that body and exit. It's local (no host or auth), it ignores
`--output` (you always get a bare JSON Schema document, not the envelope), and the allow-list and
`--readonly` never block it. For the schema nouns (`document-type`, `media-type`, `member-type`,
`data-type`, `template`) the schema comes from the Management API spec. An `update` schema
requires no key, because the body is merged.

Those nouns also take `--example`, which prints a **real** item from the instance (or a minimal
valid body on a site that has none). It's usually the best starting point for a body you'll edit.
It needs a host.

For content, `content create --example --document-type <alias>` prints a create body for that
type with one `values[]` entry per property (compositions included). Each entry holds an example
of the value shape its editor takes, plus the `editorAlias` it was chosen by. An editor it doesn't
know gets `"value": null` - look that one up rather than guessing. Ids you don't need to choose
(a media picker entry's `key`) are already fresh GUIDs. Replace the remaining `<...>`
placeholders (`<media id>`, `<document id>`), then pass the file to `content create --json-body`,
which ignores `editorAlias`.

```bash
umbraco content create --schema          # the shape of a content-create body
umbraco document-type update --schema    # the shape of a document-type update body
umbraco document-type create --example   # a real document type to start from
```

Feed the schema to a validator, or use it to build a valid body before sending.

---

## 3. The output contract

Every successful command prints this envelope on **stdout**:

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
the collection runs out and reports `hasMore: false`, or fails with `invalid_argument` past 10,000
items rather than truncating. `--all` can't be combined with `--skip`/`--take`.

`total`, `skip`, `take` and `hasMore` are **left out when the source can't report them**. An
absent `hasMore` means "unknown", not "no", so never read a missing `total` as a complete list.
Some commands (`content tree`, `content find --path`, `manifest list`) genuinely can't count, and
say so by leaving the field out.

`--output csv` can't carry `meta`, so a truncated CSV prints the same "showing N of M" line to
stderr that human output does. Stdout stays loadable as-is.

**Every write returns `data` too.** `create`, `update`, `copy` and `upload` return the resulting
item, as `get` shows it. Every other write - delete, move, trash, restore, publish, sort and so
on - returns `{ "id": ... }` (or `{ "ids": [...] }`) of what it acted on. A write with no target
returns the state it left (`redirect tracking enable` returns the tracking status), or `{}` when
there's nothing to read back (`empty-recycle-bin`). The confirmation text ("Deleted.") is human
output only.

`content publish` returns `{ id, published, publishAt, unpublishAt, cultures }`, all five always
present, so a scheduled publish reads as `"published": false`. `cultures` lists the cultures it
published: the ones named with `--culture`, or every culture the document has. It's `null` for an
invariant document, which is published whole even when `--culture` names one. `publishAt` and
`unpublishAt` are `null` when nothing was scheduled. `content unpublish` returns
`{ id, cultures }` on the same rule, except that `cultures` lists only the cultures that were
published and so went offline: a culture already in Draft is left out, and `[]` means nothing was
live. Both read the document first, and fail with a 404 without writing anything when they can't.

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

`exitCode` is the process exit code. `httpStatus` is the server's status, and it's **absent when
the request never reached the server** (a policy refusal, an unreachable host, a timeout).

**Every error carries a `category`**, so you can tell whose problem it is without parsing the
message. An error from an Umbraco API call also carries the `serverVersion` when the server
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

When Umbraco explains the failure, its answer is passed through. `message` is built from it (what
failed, Umbraco's `operationStatus`, the reason, and which properties to fix), and `details`
carries the body exactly as Umbraco sent it (its ProblemDetails, including any stack trace in
`details.detail`):

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

For an API call, `category` is one of:

- `unreachable` - no response (DNS or connection).
- `timeout`.
- `request_rejected` - a 4xx, usually bad input or the request itself.
- `server_error` - a 5xx or an undeclared status, so a server-side fault.
- `unexpected_response` - the server answered, but with a body the CLI couldn't read (not JSON,
  or a `content get`/`media get` with no named variant). That's likely a version mismatch. It has
  no `httpStatus`, and its message names the tested version range when `serverVersion` is outside
  it, or points you at `auth doctor`. Treat it as "don't trust this instance's output until the
  version is checked", not as bad input.

`serverVersion` is left out when the server couldn't be reached (`unreachable`/`timeout`) or the
version couldn't be determined.

The rest never reach the API, so they carry no `httpStatus` and no `serverVersion`:

| `category` | Exit | Meaning |
|---|---|---|
| `invalid_argument` | 1 | Your input: a command line that doesn't parse, a malformed or contradictory `--json-body`, two inputs for one value, an alias or name that matches nothing (or several items - the message lists their ids), a value the instance doesn't recognise (a webhook `--event` alias, a dictionary ISO code), a file that isn't there. |
| `internal` | 1 | An unexpected error inside the CLI - a bug to report. |
| `not_authenticated` | 2 | No host, no credentials, an unknown `--profile`, or authentication failed. |
| `not_allowed` | 2 | The command isn't in the allow-list. |
| `readonly` | 2 | A write blocked by `--readonly` / `UMBRACO_READONLY`. |
| `confirmation_required` | 2 | A destructive command run non-interactively without `--yes`. |
| `refused` | 2 | A pre-flight check refused: a delete that would take or orphan something else (an in-use type, data type or template, a group with members or users, a dictionary item with children, any language) without `--force`. |
| `cancelled` | 2 | You declined the confirmation prompt. |

A write run with `--dry-run` uses its own status and doesn't touch the server:

```json
{ "status": "dry-run", "data": { "method": "POST", "url": ".../webhook", "body": { }, "then": [] },
  "meta": { "command": "webhook.create", "durationMs": 12, "timestamp": "...", "schemaVersion": "6" } }
```

`data` is the first request. `then` lists the requests a multi-step write sends after it, each
`{method, url, body}` (for example the change-password step of `user create --password`), and is
empty for a single request. Passwords, tokens and other secrets are redacted, as under `-v`.

To see what was actually sent and received when a call fails, add `-v`. Every request and
response is logged to **stderr** (stdout stays parseable) with its body - the response cut at
4 KB - and credentials redacted.

### Contract stability rules

- The field names above (`status`, `data`, `meta`, `command`, `durationMs`, `schemaVersion`,
  the error `exitCode`/`httpStatus`/`message`, and the error `category`/`serverVersion`) are part
  of the contract and are never renamed silently.
- `meta.schemaVersion` (currently `"6"`) is bumped **only** on a breaking change - a renamed or
  removed field, or a changed meaning. New fields can appear without a bump.
- So **ignore unknown fields**, and if you want to be defensive, gate on `meta.schemaVersion`.
- **`list` results carry the same objects as the matching `get`** - same field names, same types.
  A boolean is a boolean, not `"True"`.
- An absent `meta` field means **unknown**, never a default. A list with no `total` is one whose
  source couldn't count, not one that's complete.

## 4. Output formats and trimming

- **JSON** is the default whenever stdout is redirected or piped. Force it anywhere with
  `--output json`.
- **`--fields a,b`** keeps only those top-level fields, in order, on each result (object or
  array item), matched case-insensitively. Handy for keeping your context small:
  ```bash
  umbraco content list --fields id,name
  ```
- **`--output csv`** prints RFC-4180 CSV (a list is one row per item; an object or scalar is a
  header plus value). Columns use the same camelCase keys, and `--fields` picks and orders them.
  Errors go to stderr as an `exitCode,httpStatus,category,message` row.
- **`--quiet` / `-q`** drops the result of writes (the confirmation and its `data`), so a
  successful write prints nothing. Reads, errors, `--dry-run` previews, a bulk run with failures,
  and exit codes still come through. Leave it off when you need a created item's id.

## 5. Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including a `--dry-run` preview). |
| `1` | The command ran and failed: an API error, invalid input (`invalid_argument`), a bulk run where some items failed, or an unexpected error (`internal`). |
| `2` | Stopped before running: not authenticated, blocked by the allow-list or `--readonly`, confirmation missing or declined, or refused by a pre-flight check. |
| `130` | Cancelled (Ctrl-C). |

The JSON error envelope reports this as `exitCode`, separately from the server's `httpStatus`. So
`exitCode: 1` with `httpStatus: 404` is an API 404, while `exitCode: 1` with no `httpStatus` never
reached the server. A parse error comes back the same way - an `invalid_argument` envelope whose
message says what was expected, e.g. `'Blog' is not valid for --parent: expected a GUID id.` - so
`-o json` stays parseable when you mistype.

Gate your automation on the exit code first, then read the envelope's `category` for why.

---

## 6. Authentication for agents

Pass credentials as environment variables (best for CI and supervised agents) or as a raw token.
No interactive login needed.

```bash
export UMBRACO_HOST=https://mysite.com
export UMBRACO_CLIENT_ID=umbraco-back-office-my-api-user
export UMBRACO_CLIENT_SECRET=<secret>
umbraco content list --output json
```

Or a one-off bearer token: `umbraco content list --token <bearer> --host https://mysite.com`.

Check the whole setup - host resolution, TLS, credentials, auth, identity, instance version - with
one self-check that reports each result as data and exits non-zero on any hard failure:

```bash
umbraco auth doctor --output json
```

Run `auth doctor` first in any new session. It turns "why did that 401?" into a labelled check
with a hint on how to fix it. Its `Supported version` check warns when the instance's Umbraco
major is outside the range this CLI was tested against. The CLI still runs, but treat its output
from that instance with suspicion. See [getting-started.md](getting-started.md) for profiles and
the rest of the auth setup.

## 7. Running non-interactively (the rules that bite)

- **Destructive commands won't run without a TTY unless you pass `--yes` / `-y`.** The permanent
  `delete` commands, every `empty-recycle-bin`, `unpublish` (which takes live content offline) and
  `redirect tracking disable` ask for confirmation interactively, and **stop with exit `2`**
  (`confirmation_required`) when piped or scripted without `--yes`. Cost alone doesn't count:
  `indexer rebuild` and `models-builder build` lose nothing and run without it. The
  `umbraco commands` catalog marks each of these `"destructive": true`, from the same declaration
  the prompt uses, so the two always agree. `content apply` and `schema apply` are destructive
  only with `--prune`, and every `update` (and `content domain set`) only with `--replace`; the
  catalog says so with `"destructiveWhen"`. Reversible writes - `move`, `copy`, `publish`,
  `redirect tracking enable` - never need `--yes`.
- **Deleting a type that content uses needs `--force` as well as `--yes`.** `data-type delete`
  (while in use), `member-type delete` (while it has members), and `document-type` /
  `media-type delete` (while any item of the type exists, counting the recycle bin, while another
  type uses it as a composition, or for an element type, whose block usage Umbraco doesn't report)
  are refused with exit `2` unless you add `--force`. The check runs **before** any confirmation
  prompt, and under `--dry-run` too, since a refusal is what a real run would do.
  `schema apply --prune` runs the same check on every type it would delete; its `--dry-run` plan
  marks those steps `needs --force`.
- **Pipe request bodies via stdin** with `-`:
  ```bash
  cat body.json | umbraco content create --json-body -
  echo '{"values":[{"alias":"title","culture":"en-US","value":"Hello"}]}' \
    | umbraco content update <id> --json-body -
  ```
- **Idempotent creates:** pass `--id <guid>` on any create. Umbraco honours a client-supplied id,
  so re-running a provisioning script doesn't create duplicates.

---

## 8. Working with content and schema

The practical details for publishing, reading, editing and authoring, plus how to make a direct
Management API call for anything the CLI doesn't cover.

### Publishing

`content publish <id>` with no `--culture` reads the document and publishes every culture it has.
Name cultures to publish a subset. `content unpublish` (and `bulk unpublish`) works the same way:
no `--culture` unpublishes every culture of a variant document, and the whole of an invariant one.

Other commands pick a sensible culture when you don't name one. `content create` and
`document-blueprint create` use the default language when the document type varies by culture.
In `content update` and `document-blueprint update`, a variant with no `culture` renames the
default-language variant; it's refused if the item has no variant in that language. Values with
no `culture` are left as sent. `document-blueprint create --from-document --name` renames every
culture.

`content version list` lists every culture's history, newest first, and tags each row with the
`culture` to pass to `content version rollback --culture`. Use `content version get <version-id>`
to read a version's values before rolling back. Choose the version by its
`isCurrentDraftVersion` / `isCurrentPublishedVersion` flags rather than by date, because the
current draft and the current published version can share a `versionDate`.

A rollback only changes the draft, and `content restore` brings an item back unpublished and last
in its parent's sort order, so neither changes the live site. Add `--publish` to either to publish
afterwards. `content restore <id>` puts the item back under the parent it was trashed from; pass
`--parent <id>` to choose another parent, or `--to-root` for the content root.

If you call the publish endpoint directly, two things are worth knowing. An empty `schedule`
object isn't "publish now" - Umbraco answers `200` and publishes nothing. And `"*"` is **not** a
wildcard: it's the invariant culture, so on a document that varies by culture it's rejected with
`400 "Cannot publish invariant culture when the document varies by culture."` List the cultures
instead.

`content publish-descendants` publishes a node and everything beneath it. It isn't a drop-in for
`publish` on a branch: it republishes already-published descendants, and
`--include-unpublished` pushes drafts nobody has reviewed.

A change that touches **only** the template doesn't mark culture variants as having pending
changes, so `publish-descendants` skips them as already published. Publish those cultures
explicitly.

### Reading content back

The by-id reads return what they fetch, so a `get -> edit -> update` round trip works through the
CLI alone:

| Command | Returns |
|---|---|
| `content get` | every field of `GET /document/{id}` under the API's keys - `documentType`, `values` (with each value's `editorAlias`), every `variant` with its `state`, `id`, `flags` and `scheduledPublishDate` / `scheduledUnpublishDate`, `template`, `isTrashed`, `flags` - plus `name`, `parent` and `urls` per culture |
| `media get` | `values` carrying `umbracoWidth`/`umbracoHeight`/`umbracoBytes`/`umbracoExtension`, plus `urls` per culture |
| `document-type`, `media-type`, `member-type`, `data-type`, `template` `get` | the Management API body verbatim - `properties`, `containers`, `compositions`, `allowedTemplates`, `collection`, allowed children, per-property `validation`; a data type's `values`; a template's `content` - which is a valid `update --json-body` as it stands |

`member get` returns the member's `groups` and `values` too. `content get` and `media get` carry
`parent` (read from the tree, because the by-id body has none; left out at the root), and so do
`dictionary get` and `document-blueprint get`. `data-type list` rows carry their folder.
`content list` rows leave out `updateDate`, `values`, `urls` and the full `variants`, because the
tree endpoint behind them doesn't return those. Read one item with `content get` when you need
them.

Type references (`documentType`, `mediaType`) carry a resolved `alias`. When it can't be resolved
the field is **left out** rather than returned as an empty string, so treat its absence as
"unknown" rather than "no alias".

Two more ways to read:

1. **`umbraco schema export`** returns the verbatim get-by-id body of every document type, media
   type, member type, data type and template - full fidelity, nothing trimmed - plus every
   language, dictionary item, member and user group, and the partial views, stylesheets and
   scripts with their content. This is the right way to read schema.
2. **A direct Management API call** for everything else, with the same credentials - see
   [Getting a token for direct calls](#getting-a-token-for-direct-calls) below.

### Editing content

`content update` **merges**: the body's values are matched on alias + culture + segment, its
variants on culture + segment, and anything you leave out keeps its current value. The template
is kept. So adding one translation is one call, with no read first:

```bash
echo '{"values":[{"alias":"title","culture":"da-DK","segment":null,"value":"Hej"}],
       "variants":[{"culture":"da-DK","segment":null,"name":"Hej"}]}' \
  | umbraco content update "$ID" --json-body -
```

`--replace` switches to the destructive behaviour: the body's values and variants replace the
item's wholesale, clearing anything absent. Use it when you're writing a document you already
hold in full. The template survives `--replace` too; change it with `--template <alias|id>`.

`--dry-run` prints the body the server will receive - the quickest way to check a merge does what
you expect.

See [commands.md](commands.md#property-value-formats-for---json-body) for the value shape each
property editor expects.

### Authoring schema

The scalar flags on `document-type create`, `data-type create/update` and
`member-type create/update` can't express properties, groups, editor configuration, templates,
compositions or culture variance. **Pass the Management API body instead:**

```bash
umbraco document-type get blogPost -o json | jq .data > t.json
# ...edit t.json: add a property, a group, a template...
umbraco document-type update blogPost --json-body t.json
```

`--json-body` is on `create` and `update` for `document-type`, `media-type`, `member-type` and
`data-type`, and on `template update`. It takes a file, or `-` for stdin. `update` **merges the
body's top-level keys** into the item, so a partial body is safe: a key you leave out keeps its
value, and a key you send replaces it whole. Pass `--replace` (with `--yes`) to send the body as
the whole item. `--schema` prints the body's JSON Schema, and `--example` prints a real item from
the instance to start from (it needs a host; on a site with none yet, it prints a minimal valid
body). Both `update` and `create` return the resulting item.

A blueprint's scaffold is a valid content body:
`document-blueprint scaffold <id> | umbraco content create --json-body -` creates a new item from
it (its `documentType` is read as the `contentType`). The scaffold carries no `id`, so each create
makes a new item; a body that does carry an `id` keeps it, in either shape.

For a **set** of types, or for moving schema between environments, use the snapshot round trip:

```bash
umbraco schema export --out schema.json     # full-fidelity bodies
# edit .documentTypes[] / .dataTypes[] - new containers and properties need no id
umbraco schema apply schema.json --dry-run  # preview the plan
umbraco schema apply schema.json
```

**A snapshot can be written by hand, and can be partial.** Keep only the sections you mean to
change: an absent section isn't managed, so diff, apply and `--prune` leave that kind alone (a
present section is the whole list for its kind). Leave `id` out of new entities, properties and
containers: each takes the id of the live one it matches (a property by alias, so its values are
kept), or a new one. Name what an entry references instead of giving its id:
`"dataType": "Textstring"`, `"container": "Content"` (or `"Content/Hero"` for a group),
`"masterTemplate": "master"`, `"compositions": [{ "documentType": "seoMixin", ... }]`. Names are
looked up in the snapshot first, then on the instance; one that matches nothing or several things
fails before anything is written. Each entry is still the whole item, so a changed type lists
every property it keeps. See [commands.md](commands.md#schema-export--diff--apply) for the full
list of references and an example.

```bash
umbraco document-type get blogPost -o json | jq '{schemaVersion: "4", documentTypes: [.data]}' > edit.json
# ...add a property with "dataType": "Textstring" and no id...
umbraco schema diff edit.json               # only blogPost is compared
umbraco schema apply edit.json
```

**Templates travel with their partials.** The snapshot carries `partialViews`, `stylesheets` and
`scripts` as `{path, content}` per file and `{path, isFolder: true}` per folder, so a promoted
template arrives with the files it renders. Apply creates the folders and files before the
templates, and writes each file byte for byte. If your views deploy from git, export with
`--no-files`: a snapshot **without** those sections doesn't manage files at all, so diff and apply
leave the target's files alone and `--prune` never deletes one. A section that's present but
empty **does** manage them: `--prune` then deletes every live file of that kind. `--prune` won't
delete a file a template names (`Html.PartialAsync("header")` names `/header.cshtml`) unless you
add `--force`. A diff row noted `line endings only` differs only in CRLF/LF or a trailing newline.

Folders for these files have their own commands:
`umbraco partial-view folder create --name Components --parent blocklist` (and `delete`, and the
same for `stylesheet` and `script`).

So do **media folders** and **domains**:

```bash
umbraco media folder create --name Blog                     # id goes to media upload --parent

# route /da/ to the Danish variant. Without domains, Umbraco logs "the root node was published
# with multiple cultures, but no domains are configured" and serves nothing but the default.
umbraco content domain set "$ID" --default en-US \
  --domain example.com=en-US --domain example.com/da=da-DK
```

The snapshot also holds media types, member types with their properties, languages, the
dictionary, and member and user groups, so one round trip promotes all of them before
`content apply`. User groups travel without their start nodes and per-document permissions, and
apply keeps the target's own. Domains stay per environment (`content domain set`).

The snapshot format is `"4"`. The CLI also reads format `"3"` files, which just have no file
sections (so they don't manage files). An older file is refused - re-export it.

### Getting a token for direct calls

A direct Management API call uses the **same API user** the CLI is configured with, so there's
no extra setup. Exchange the client credentials for a bearer token at the same endpoint the CLI
uses:

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

Tokens are short-lived, so fetch one per script run rather than storing it. A direct call skips
every guardrail in section 9 - `--readonly` and the allow-list constrain the CLI, not `curl`. If
you're supervising an agent, that's a good reason to prefer a CLI command where one exists.

## 9. Guardrails (for whoever supervises the agent)

Two settings limit what a session can do. They work best set by the **parent process** that
supervises an agent, where the agent itself can't change them.

### Read-only mode

`--readonly` (or `UMBRACO_READONLY=1`) refuses every write - any non-GET request, so `sort`,
`health run` and the like as well as create/update/delete/publish - with exit `2` and category
`readonly`. Reads are unaffected. Every write is marked `"mutating": true` in `umbraco commands`.

### Command allow-list

`UMBRACO_ALLOWED_COMMANDS` (or the config `allowedCommands` field) limits which commands can run.
Entries are noun groups (`content`, `media`) and/or full command names (`content.list`); a command
runs only if its group or full name is listed. The `auth` group is always allowed. A blocked
command stops before running with exit `2` and category `not_allowed`.

- **Unset vs lockdown:** only a *truly unset* value (variable absent and no config
  `allowedCommands`) means no restriction. Any *present* value that's blank or separators-only
  (`" "`, `","`) is a lockdown - nothing runs but the always-allowed `auth` group.
- **Tighten-only:** every list in force applies, and a command must pass all of them:
  `UMBRACO_ALLOWED_COMMANDS`, and the `allowedCommands` of **every** profile in the default config
  file (and, with `--config`, in that file too). So a list set on any profile applies to the whole
  file, whichever profile is selected. Selecting another profile (`--profile`, `UMBRACO_PROFILE`),
  changing the default (`auth profile use`), logging in to a new profile, pointing `--config` at
  another file or setting the environment variable can only ever *tighten* access, never widen
  it. To loosen a file's list, edit the file.

```bash
# An agent that may only read content and media, and never write:
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media umbraco content list
```

**Where it's enforced:** the environment-variable forms are the real boundary, so set them in the
supervising process. The config-file `allowedCommands` holds against the session's own arguments,
but not against anything that can write the config file itself.

### Where credentials are sent

The CLI sends stored or `UMBRACO_CLIENT_*` credentials only to the host they were configured with,
and never over plain `http://` except to loopback. An agent that passes `--host` for another
instance gets exit `2` (category `refused`) unless it also supplies its own `--token`. See
[commands.md](commands.md#where-credentials-are-sent).

### Preview writes

`--dry-run` on any write prints the requests it would send (method, URL, body; secrets redacted)
and exits `0` without changing anything (`"status": "dry-run"`). Use it to show a plan before
committing to it.

---

## 10. Recipes

### Provision idempotently

Create with a fixed `--id` so re-runs converge instead of duplicating:

```bash
umbraco document-type create --name "Blog Post" --alias blogPost
umbraco content create --document-type blogPost --name "Hello" --id 3f2a...  # same id each run
```

### Provision a backoffice user and its group

`user create` needs no SMTP (unlike `user invite`). Give the group its permissions, then give the
user its password in the same create. A rejected password deletes the new user again, so a failed
run can simply be repeated:

```bash
umbraco user-group create --alias blogEditors --name "Blog editors" --section Umb.Section.Content \
  --document-start-node "$BLOG_ID" --document-permission "$BLOG_ID=Umb.Document.Read,Umb.Document.Update"
umbraco user create --email jane@example.com --name "Jane" --group blogEditors --password "$PASSWORD"
umbraco user get jane@example.com            # userGroups as [{id, alias, name}], sections, start nodes
umbraco user update jane@example.com --disabled   # --disabled false enables again; --unlock clears a lockout
```

Users are named by id, email or username. Umbraco hides the super-user from every other user, so
it's missing from `user list` and can't be found by email unless you're signed in as it.

### Move schema between environments

Export every schema entity - document types, media types, member types, data types, templates,
languages, dictionary items, member and user groups, and the partial views, stylesheets and
scripts the templates render - to a portable snapshot, diff it against a target, then apply. See
[commands.md](commands.md#schema-export--diff--apply).

```bash
umbraco schema export --out schema.json                 # from source
umbraco schema diff schema.json                         # against target (read-only)
umbraco schema apply schema.json --dry-run              # preview the whole plan
umbraco schema apply schema.json                        # create + update (never deletes)
umbraco schema apply schema.json --prune --yes          # also delete what the snapshot omits
```

### Move or sync content between environments

The content pipeline works like the schema one. See
[commands.md](commands.md#content-export--diff--apply).

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

A media snapshot is a directory (`media.json` plus `files/`), so it can't be piped.

### Bulk operations from a query

Bulk commands read ids from `--file` or stdin, and report each id on its own in the `data`
results array (`{id, status, error}`). The exit code is `1` if any item failed. The envelope's
`status` says how the batch went - `success`, `partial`, `error` (every item failed) or
`dry-run` - with the counts in `meta.summary`, and it's written to stdout either way. Under
`--dry-run` each item carries the `request` it would have sent.

The input can be the CLI's own output, so a list pipes straight in with no `jq`:

```bash
umbraco content list --fields id | umbraco content bulk publish
```

The format is read from the content. JSON (first character `{` or `[`) can be the success
envelope, whose `data` items each give their `id`, an array of objects with `id`, or an array of
id strings. CSV is recognised by a header row with an `id` column (`-o csv`), and that column is
used. Anything else is one id per line. JSON items without an `id`, or a CSV row with an empty
`id`, refuse the whole batch as `invalid_argument` (exit 1) before anything runs; an id that isn't
a GUID is still reported per item.

### A read-only audit agent

```bash
UMBRACO_READONLY=1 UMBRACO_ALLOWED_COMMANDS=content,media,server,health,log-viewer \
  umbraco auth doctor && umbraco server info && umbraco health list
```
