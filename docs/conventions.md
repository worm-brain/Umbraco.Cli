# CLI surface conventions

The design rules for the `umbraco` command surface: nouns, verbs, arguments, options, output,
errors and exit codes. It holds rules, not history. When this file and an ADR, plan or issue
disagree, this file wins; the older document is rationale that may be out of date.

- **Sources of truth, in order:** this file > [`surface.json`](surface.json) (the current surface,
  generated from the code) > ADRs, plans and issue bodies.
- **Tiebreaker** for anything not covered: [clig.dev](https://clig.dev), then what `gh` does, then
  `az`.
- **Alpha rule:** breaking changes are fine; inconsistency is not. Doing it properly may mean
  changing several commands at once - list them all and change them together, not one-offs.
- **Changing a rule:** an agent may propose a change; only the maintainer accepts one. Record an
  accepted change in the [changelog](#changelog) at the bottom with its date and a one-line reason.

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
   | Replace instead of merge | `--replace` |
   | Override a refusal | `--force` |
   | Confirm a destructive run | `--yes` / `-y` (global) |

2. **One name, one meaning.** A name never means different things on different commands.
3. **Multi-value options are singular and repeatable** (`--culture en-US --culture da-DK`), and also
   accept commas, except `key=value` options, which are repeat-only. Pairs are `--value key=value`.
4. **Booleans:** a flag names the non-default behaviour (`--desc`, `--asc`, `--no-state`). On
   `update`, an omitted flag leaves the value unchanged; `--flag false` clears it.
5. **Inputs:** `-` means stdin on every file-valued input. Two inputs that conflict
   (`--content` with `--content-file`, a `--json-body` id that contradicts `--id`) are an
   `invalid_argument` error, never a silent precedence.
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
   only. `--quiet` drops write results along with the confirmations.
3. `meta.command` is the dotted command path (`document-type.list`), derived from the command
   tree, never typed by hand. The allow-list matches the same name.
4. A breaking change to the envelope or a payload shape bumps `meta.schemaVersion`.

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

## 8. Help text

1. The summary (first line) is imperative, at most 80 characters, and ends with a full stop.
2. Every option and argument has help text. Help text never cites issue numbers.
3. Every leaf command has an `Examples:` block.
4. One term per concept: "id" in prose (`guid` is the type name), "document type",
   "backoffice", "ISO code".
5. Every example parses. A test runs each `Examples:` line through the real parser, so an
   example must be a real command line, with these placeholders and no others:
   - a truncated id, eight hex digits then `-...` (`3f7a8b2e-...`), for any id;
   - `<id>`, `<guid>`, `<folder-id>`, `<version-id>`, `<relation-type-id>`, `<section-id>`
     (kebab-case) for an id, and `<secret>` for a secret;
   - other values written out as real ones (`--name "Blog Post"`, `--culture en-US`).

   Shell around the command is fine: a trailing `# comment`, a pipe into or out of another
   program (`| jq ...`, `cat ids.txt |`), and a `> file` redirect are cut before parsing. A line
   that is only a comment is allowed. Adding a placeholder means adding it to the list here and
   in `HelpTextTests`.

These rules are checked by unit tests over the command tree. The same tests check that every
command path and option named in a `docs/commands.md` synopsis exists.

## 9. Known exceptions

Each exception is deliberate or tracked; don't copy it.

- `user-group add-users` / `remove-users` are verb-noun compounds (membership has no better home
  yet).
- `property-type is-used` is a top-level noun with one verb and no `list`/`get`.

## Changelog

- **2026-09-25** - First version, from the
  [surface audit](surface-audit-2026-09-25.md) and decisions D1-D8 recorded there.
- **2026-09-25** - 6.2 made precise while applying it (#268): which writes return the item
  (create/update/copy/upload) and which return `{ "id" }`, what a write with no target returns,
  and that `--quiet` drops write results.
- **2026-09-27** - 2 and 4.5: `media export/diff/apply` (#226) takes a snapshot **directory**, so
  its `--out` is required and names a directory, and its `<snapshot>` does not accept `-`. The
  files are binary and cannot travel in the envelope or on stdin.
- **2026-09-27** - 5.2 applied to the remaining cascading deletes (#269). Template, member-group,
  user-group and dictionary deletes are refused while something uses them or they have
  children; language delete always needs `--force`. `schema apply --prune` runs the same checks.
  The section 9 exception is removed. Folder deletes are not guarded, because Umbraco already refuses a
  non-empty folder.
- **2026-09-27** - 8.5: examples must parse, from a closed placeholder vocabulary; checked by
  `HelpTextTests`, which also checks the command paths and options in `docs/commands.md`
  (#250 Phase 7).
- **2026-09-28** - 7: errors carry `details`, Umbraco's ProblemDetails body as sent, and the
  message is built from it (#286). Chosen over a curated field set so new Umbraco fields are
  never dropped.
- **2026-09-28** - 2 (`export`/`diff`/`apply`): the schema snapshot, format "4", carries partial
  views, stylesheets and scripts (#292). A section that is **absent** means that kind is not
  managed: diff and apply skip it and `--prune` never deletes one. So format "3" files and
  `schema export --no-files` leave the target's files alone, and a section that is present but
  **empty** does manage them. Chosen so sites that deploy views from git keep doing so.
- **2026-09-28** - 4.1 and 5.3: every paged command takes `--all` (#196), added once in
  `PagingOptions` and run by the executor, so it cannot differ between commands. A real loop with
  a loud 10,000-item cap, not a large `--take`, which would be the same silent cap further out.
- **2026-09-28** - 4.7: conditionally required inputs are declared with `.RequiredUnless(...)`
  (#84), so the catalog tells an agent what it needs instead of reporting them optional. The
  catalog also gains each input's `default` and a `jsonBodySchema` pointer; both additive, so no
  `schemaVersion` bump.
- **2026-09-28** - 4.1: `content create --example --document-type <alias>` (#174) builds its
  example from the document type and its data types, since there is no one real item whose values
  show every editor. Still `--example`, not a per-type `--schema`, which stays the offline schema.
