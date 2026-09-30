# CLI surface conventions

The design rules for the `umbraco` command surface: nouns, verbs, arguments, options, output,
errors and exit codes. It holds rules, not history. When this file and an ADR, plan or issue
disagree, this file wins; the older document is rationale that may be out of date.

- **Sources of truth, in order:** this file > [`surface.json`](surface.json) (the current surface,
  generated from the code) > ADRs, plans and issue bodies.
- **Tiebreaker** for anything not covered: [clig.dev](https://clig.dev), then what `gh` does, then
  `az`.
- **Consistency first:** doing it properly may mean changing several commands at once - list
  them all and change them together, not one-offs.
- **Changing a rule:** an agent may propose a change; only the maintainer accepts one. Edit the
  rule in place; the issue and commit carry the reason.

## 1. Grammar and names

1. A command is `umbraco <noun> [<sub-noun>] <verb> [<id>...] [--options]`. Every command, option and
   argument name is lower-case kebab-case.
2. **Nouns are singular**: `document-type`, `media-type`, `user`, `template`, `language`,
   `webhook`. Mass and subsystem nouns stay as they are: `content`, `media`, `dictionary`,
   `schema`, `server`, `health`, `log-viewer`, `models-builder`, `imaging`, `user-data`, `auth`.
3. **Umbraco's terms, as the backoffice shows them:** `content` for content items (the section),
   `document-type` / `media-type` / `member-type` for the types, `document-blueprint` for
   blueprints. Never "content type" for a document type. Spell it "backoffice".
4. A **sub-noun** groups the verbs about one sub-resource of its parent: `content domain`,
   `content version`, `media folder`, `redirect tracking`, `log-viewer saved-search`. A top-level
   noun has at least a `list` or a `get`.

## 2. Verbs

| Verb | Meaning |
|---|---|
| `list` | A page of a collection, or one level of a tree (`--parent`). |
| `get` | One item, by selector. Never `show`, `info` or `status`. |
| `tree` | A recursive walk from a root, bounded by `--depth`. Not paged. |
| `find` | Search by text. |
| `create` / `update` / `delete` | Write one item. Creating from something else is an option on `create` (`--from-document`), not a new verb. |
| `move` / `copy` / `sort` | Placement. `move`, `copy` and `restore` take `--parent` (alias `--target`); no parent means the root. |
| `trash` / `restore` / `empty-recycle-bin` | The recycle bin. `restore` goes back to the original parent; `--to-root` overrides. |
| `publish` / `unpublish` | Content state. |
| `export` / `diff` / `apply` | Snapshot pipelines: `export [--out\|-O <file>]`, `diff <snapshot>`, `apply <snapshot> [--prune]`. `media`'s snapshot is a directory: `--out <dir>` is required and `-` is not accepted. |

- A verb that is a real Umbraco action keeps its name (`media upload`, `user invite`,
  `indexer rebuild`, `models-builder build`).
- The first word of a command's help text is its verb ("Delete a language.", not "Remove ...").

## 3. Arguments: selecting what a command acts on

1. **One target is a positional `<id>`.** It is always named `id`, whatever keys it accepts.
2. A selector accepts the GUID plus every natural key the entity has (alias, then name, ignoring
   case; a language's ISO code; a dictionary item's key). Its help text says which. Its type is
   `guid` when only a GUID is accepted, otherwise `string`.
3. **Several targets known up front are a variadic positional**: `user-group delete <id>...`,
   `imaging resize-urls <id>...`. There are no `*-many` verbs and no `--ids` options.
4. **Many targets from a stream** (stdin or a file, with per-item results) use the `bulk`
   sub-noun: `content bulk delete|publish|unpublish`.
5. Filters and references to *other* entities are options, not positionals.

## 4. Options

1. **One concept, one name, everywhere:**

   | Concept | Option |
   |---|---|
   | Parent / placement target | `--parent` (alias `--target` on placement verbs) |
   | ISO culture code | `--culture` (repeatable when several are allowed) |
   | A type reference | `--document-type`, `--media-type`, `--member-type`, `--relation-type`; `--type` only where any kind of type is accepted |
   | Paging | `--skip` (default 0), `--take` (default 100), `--all` (every page) |
   | Idempotent create | `--id <guid>` |
   | Request body | `--json-body <file\|->`, with `--schema` (its JSON Schema) and, where a schema cannot say enough, `--example` (a real one from the instance, or one built from it: `content create --example --document-type <alias>`) |
   | Output file | `--out` / `-O` |
   | Inline file text vs file | `--content <text>` / `--content-file <file\|->` |
   | Inline pair value vs file | `--value key=value` / `--value-file key=<file\|->` |
   | Replace instead of merge | `--replace` |
   | Override a refusal | `--force` |
   | Confirm a destructive run | `--yes` / `-y` (global) |

2. **One name, one meaning.** A name never means different things on different commands.
3. **Multi-value options are singular and repeatable** (`--culture en-US --culture da-DK`), and also
   accept commas, except `key=value` options, which are repeat-only. Pairs are `--value key=value`,
   and `--value-file key=<file|->` where the value is read from a file (UTF-8, stored exactly as
   the file holds it). A key takes one value, so naming it in both `--value` and `--value-file`,
   or in two `--value-file` pairs, is refused.
   An **empty value** (`key=`) means "nothing for this key". Where the value is data that may be
   empty (a property value `--value alias=`, a translation `--value en-US=`) it is set empty,
   which clears it. Where an entry cannot exist without its value (`--header name=`,
   `--document-permission <id>=`, `--domain host=`) it removes that entry; on a create there is
   nothing to remove, so it is ignored. On `--value-file` the value is a path, so `key=` names no
   file and is refused; `--value key=` sets the value empty. An empty *key* (`=value`) is always
   refused. Each option's help says what `key=` does.
4. **Booleans:** a flag names the non-default behaviour (`--desc`, `--asc`, `--no-state`). On
   `update`, an omitted flag leaves the value unchanged; `--flag false` clears it.
5. **Inputs:** `-` means stdin on every file-valued input. Stdin can be read once, so at most one
   input per command is `-`. Two inputs that conflict (`--content` with `--content-file`, a
   `--json-body` id that contradicts `--id`, the same key in `--value` and `--value-file`, a second
   `-`) are an `invalid_argument` error, never a silent precedence.
6. **Short aliases** belong to global options (`-H -o -q -v -y -p`), plus `-O` for `--out`. An
   alias never means two things.
7. **Conditionally required inputs** (required unless `--json-body`, `--schema`, ...) are optional
   at parse level, enforced by the command's validator, and declared with `.RequiredUnless(...)`
   so `umbraco commands` reports them as `requiredUnless` rather than a bare `required: false`.

## 5. Behaviour

1. **`update` merges:** it reads the item, lays the given fields over it and writes it back, so an
   omitted field keeps its value. `--replace` sends the item as given and drops what is missing.
2. **Safety is declared per command**, never inferred from the verb:
   - Every leaf that sends a write declares it (`.Mutating()`), and so appears as
     `mutating: true` in `umbraco commands`. `--readonly` blocks every non-GET request.
   - **Destructive** (gated behind `--yes`) means it can lose data the CLI cannot restore, or it
     takes live content or a site feature offline. Cost alone does not gate. A flag that makes
     an otherwise safe command destructive is declared with `DestructiveWith` (`--replace`,
     `--prune`) and shows as `destructiveWhen`.
   - A delete that would remove or orphan *other* entities is refused without `--force`, and the
     check runs before the confirmation prompt.
3. **Paging:** every paged command uses the shared `PagingOptions`, and its output carries
   `meta.total/skip/take/hasMore` (omitted when unknown). A complete list or walk is not paged.
   `--all` pages until the collection is exhausted and reports `hasMore: false`; it fails with
   `invalid_argument` past 10,000 items rather than truncating, and combined with an explicit
   `--skip` or `--take` it is a parse error.

## 6. Output

1. JSON is the default when stdout is not a terminal. Every result is the envelope
   `{status, data, meta}`; `meta` always has `command`, `timestamp`, `durationMs` and
   `schemaVersion`.
2. **Every success has `data`.** `create`, `update`, `copy` and `upload` return the resulting item,
   as `get` would show it. Every other write (delete, move, trash, restore, publish, sort,
   membership...) returns `{ "id": ... }` or `{ "ids": [...] }` of what it acted on; one with no
   target returns the state it left (e.g. `redirect tracking status`), or `{}` when there is
   nothing to read back (`empty-recycle-bin`). A human-readable message is for `--output human`
   only. `--quiet` drops write results along with the confirmations, so a successful write
   (`mutating: true` in `umbraco commands`) prints nothing; reads, errors, `--dry-run` previews
   and a bulk run with failures still print. The exception is a write whose result is a report the
   caller ran it for (`health run`): it still prints under `--quiet`.
3. `meta.command` is the dotted command path (`document-type.list`), derived from the command
   tree, never typed by hand. The allow-list matches the same name.
4. A breaking change to the envelope or a payload shape bumps `meta.schemaVersion`.
5. **A command may add its own `meta` fields** for facts about the site rather than the item, such
   as `dictionary get`'s `valueFormat`. It declares them with `WithMeta` (`CommandMeta`), never in
   its action, so they are read only after the call succeeds and never under `--dry-run`. An own
   field is absent when its value is unknown, like every other absent field.

## 7. Errors and exit codes

| Exit | Meaning |
|---|---|
| `0` | Success, including a `--dry-run` preview. |
| `1` | The command ran and failed: an API error, invalid input, a bulk run where some items failed, or an unexpected error. |
| `2` | Aborted before running: not authenticated, blocked by the allow-list or `--readonly`, confirmation missing, or refused by a pre-flight check. |
| `130` | Cancelled. |

- The codes come from one `ExitCode` enum.
- **Every error carries a `category`**, from one set: `unreachable`, `timeout`,
  `request_rejected`, `server_error`, `unexpected_response`, `invalid_argument`,
  `not_authenticated`, `not_allowed`, `readonly`, `confirmation_required`, `refused`,
  `cancelled`, `internal`.
- Errors go to stderr as `{status: "error", exitCode, httpStatus?, message, category, details?,
  meta}`.
- **Pass Umbraco's answer through.** When Umbraco returns an error body (ProblemDetails),
  `details` is that body as sent, and `message` is built from it (`title`, `operationStatus`, the
  first line of `detail`, `invalidProperties`, `errors`), never from a stack trace.
- **Credentials only go where they belong.** A plain `http://` host is refused unless it is
  loopback, and stored or `UMBRACO_CLIENT_*` credentials are only sent to the host they were
  configured with, so a different `--host` needs `--token`. Both abort with exit `2` and category
  `refused`.

## 8. Help text

1. The summary (first line) is imperative, at most 80 characters, and ends with a full stop.
2. Every option and argument has help text. Help text never cites issue numbers.
3. Every leaf command has an `Examples:` block. Examples are data: declare them with
   `.WithExamples("umbraco ...", ...)` (`CommandExamples` in `src/Umbraco.Cli/Infrastructure`),
   which renders the block into the help and lists the lines in `umbraco commands` as `examples`.
   Never write the block into the description by hand.
4. One term per concept: "id" in prose (`guid` is the type name), "document type",
   "backoffice", "ISO code".
5. Every example parses. A test runs each declared example through the real parser, so an
   example must be a real command line, using only the placeholders defined in
   `ExamplePlaceholders` (`src/Umbraco.Cli/Infrastructure/ExamplePlaceholders.cs`), the one list
   of them. Today that is:
   - a truncated id, eight hex digits then `-...` (`3f7a8b2e-...`), for any id;
   - `<id>`, `<guid>`, `<folder-id>`, `<version-id>`, `<relation-type-id>`, `<section-id>`
     (kebab-case) for an id, and `<secret>` for a secret;
   - other values written out as real ones (`--name "Blog Post"`, `--culture en-US`).

   Shell around the command is fine: a trailing `# comment`, a pipe into or out of another
   program (`| jq ...`, `cat ids.txt |`), and a `> file` redirect are cut before parsing. A line
   that is only a comment is allowed. Adding a placeholder means adding it to
   `ExamplePlaceholders`; the test reads it from there.

These rules are checked by unit tests over the command tree. The same tests check that every
command path and option named in a `docs/commands.md` synopsis exists.

## 9. Known exceptions

Each exception is deliberate or tracked; don't copy it.

- `user-group add-users` / `remove-users` are verb-noun compounds (membership has no better home
  yet).
- `property-type is-used` is a top-level noun with one verb and no `list`/`get`.
- `completion <shell>` and `commands` are local tool commands, not resource nouns: they have no
  verb, and `completion`'s positional is `shell`, not `id`. `completion` prints a bare shell
  script, never the envelope, because a shell sources it.
