# `umbraco` CLI surface consistency audit

> **Status: applied** by the #268 batch. This is a dated record: command names below are the ones
> in use when it was written. The rules it led to live in [conventions.md](conventions.md); the
> current surface is [surface.json](surface.json). Out of scope and still open: I5 (#269), I10
> (#237, #216, #214).

Input for `docs/conventions.md` and the breaking-change batch that follows it. Written 2026-09-25 against `docs/surface.json` in worktree `surface-snapshot` (snapshot commit 3399408).

## How this was done

- **Counts (parsed from `surface.json`):**
  - 35 top-level nouns, plus the root leaf `commands`.
  - 7 sub-nouns: `content domains`, `content bulk`, `media folder`, `data-types folder`, `document-blueprint folder`, `log-viewer saved-search`, `redirect tracking`.
  - **183 leaf commands** built from 55 distinct verbs.
  - **356 command options** (87 distinct names), plus 11 global options.
  - **95 positional arguments**.
- **Method:**
  - Python scripts flattened the tree and tabulated every option name (type, required, hasDefault, aliases, help text), every argument, every verb and the mutating/destructive flags.
  - Source was read to fill what the snapshot does not capture:
    - `CommandCatalog.MutatingVerbs`, `CommandSafety`, `CommandExecutor`, `JsonOutputWriter`, `CsvOutputWriter`, `ParseErrorReporter`, `FailureCategory`, `PagingOptions`, `FileContentInput`, `MutationInterceptorHandler`.
    - Every `executor.RunXxxAsync` call site, to learn each command's output shape.
  - `spec/management.json` was checked for the HTTP method behind commands whose classification looked doubtful.
  - `commands.md`, `agent-guide.md`, `getting-started.md`, `README.md`, `glossary.md` and `CONTEXT.md` were scanned by script: every `umbraco <path> --opt` in them was checked against the tree.
- **Notation:** `M` = `mutating: true`, `D` = `destructive: true`, `J` = `acceptsJsonBody`.

---

## Already consistent: candidate conventions (no debate needed)

Each rule below is followed everywhere, or with the outliers named under Inconsistencies.

1. **Grammar.** Commands are `umbraco <noun> [<sub-noun>] <verb>`. Every command and option name is lower-case kebab-case.
   - Only outlier: the argument `documentId` (see I11).
2. **A single target is positional.** All 23 `get`, 23 `delete`, 17 `update`, 5 `move`, 2 `copy`, 2 `trash` and 2 `restore` commands take their target as a positional argument.
3. **Argument type tells you what is accepted.**
   - Typed `guid` when only a GUID is accepted: 36 `id` arguments.
   - Typed `string` when an alias, name or key also resolves: 25 `id` arguments. The help text then says "its id, or its alias/name".
4. **Paging option names.** Every paged command (36 of 36) uses `--skip`/`--take`, and `--skip` defaults to 0. There is no `--page`, `--limit` or `--offset` anywhere.
5. **Idempotent create uses `--id <guid>`.** It is present on 17 of the 19 commands that create a GUID-keyed entity, counting `media upload` and `from-document`. Outliers are in I11.
6. **Placement verbs.** `move`, `copy` and `restore` take `--parent`, alias `--target`, and an omitted parent means the root (9 of 9). The one semantic outlier is `content restore` (I16).
7. **Delete is always gated.** All 23 `delete` commands, plus `user-groups delete-many` and `content bulk delete`, are `M D`. So are both `empty-recycle-bin` commands and both `unpublish` commands.
8. **Deleting an in-use type needs `--force`.** This covers `media-types`, `content-types`, `data-types` and `member-types delete`, plus `schema apply --prune`. The check runs before the confirmation prompt.
9. **Help-text punctuation and voice.**
   - All 225 command descriptions, and every option or argument description that exists, start with a capital letter and end with a full stop.
   - Leaf commands use the imperative ("List ...", "Get ...", "Create ...").
10. **Multi-value options take repeats or commas.** Commas are not accepted by the `key=value` options (`--value`, `--values`, `--domain`). This is implemented once and documented once (`commands.md`, "Lists and references").
11. **Snapshot pipelines share one shape.**
    - `export [--out|-O <file>]`, `diff <snapshot>` and `apply <snapshot> [--prune]`.
    - `-` means stdin.
    - `destructiveWhen: "--prune"` is set on both `apply` commands.
12. **Date options.**
    - Ranges use `--start-date`/`--end-date` (3 of 3).
    - Scheduling uses `--publish-at`/`--unpublish-at`.
13. **Short aliases belong to global options.**
    - The global aliases are `-H`, `-o`, `-q`, `-v`, `-y` and `-p`.
    - The only command-level alias is `-O` (on `--out`, 2 commands).
    - No alias means two different things anywhere.
14. **`--json-body` always comes with `--schema`.** 8 of 8, and both options take a path or `-`.
15. **Envelope core.** Every JSON result has `status`, `data` and `meta{command, durationMs, timestamp, schemaVersion}`.
    - Lists add `meta.total/skip/take/hasMore`, and omit them when unknown.
    - Errors go to stderr with `exitCode`, `httpStatus` and `category`.
    - `meta.command` is the dotted command path.

---

## Inconsistencies, grouped by pattern

### I1. Singular vs plural nouns → maintainer decision (D1)

| Form | Nouns | Leaves |
|---|---|---|
| Plural (13) | `media-types`, `content-types`, `data-types`, `languages`, `templates`, `members`, `member-types`, `users`, `webhooks`, `member-groups`, `tags`, `cultures`, `user-groups` | 62 |
| Singular (11) | `script`, `stylesheet`, `partial-view`, `document-blueprint`, `redirect`, `relation-type`, `relation`, `indexer`, `searcher`, `manifest`, `property-type` | 42 |
| Mass or subsystem (11) | `content`, `media`, `dictionary`, `schema`, `server`, `health`, `log-viewer`, `models-builder`, `imaging`, `user-data`, `auth` | 78 |

- There is no dominant form. The singular nouns mirror the Management API path segments exactly (`/script`, `/relation-type`, `/indexer`).
- Sub-nouns mix both forms too:
  - Singular: `folder` ×3, `saved-search`, `bulk`, `tracking`.
  - Plural: `domains`.
- **Breaking.** A rename also changes `meta.command` values and **allow-list entries** (`UMBRACO_ALLOWED_COMMANDS=content-types`). Supervisors' configs break, so call this out in the release notes. ADR 0007 is touched.
- Effort: M. The renames themselves are mechanical; the docs and integration assertions are the bulk of the work.

### I2. "content" vs "document" → maintainer decision (D2)

- **Umbraco's own terms:** the API says `document` and `document-type`. The backoffice says "Content" (the section) and "Document Types".
- **The CLI mixes the two:**
  - Uses "content": the noun `content` and the noun `content-types`, whose help text says "document types". The options `content create --content-type` and `property-type is-used --content-type` also use it.
  - Uses "document": the noun `document-blueprint` and the verb `document-blueprint from-document <documentId>`. So do the options `document-blueprint create --document-type`, `user-groups create|update --document-start-node` and `user-groups create|update --document-root-access`.
  - The `content domains` help text says "document's".
- Help text counts: "document" 34, "content item" 19, "document type" 10, "content type" 1.
- Breaking. Effort: S for types and options only; L if `content` is renamed.

### I3. Writes that the catalog reports as `mutating: false`

- **The mechanism:**
  - `mutating` comes from the `MutatingVerbs` verb set.
  - `--readonly` is enforced at the HTTP layer: every POST/PUT/PATCH/DELETE is blocked.
  - So `--readonly` does block these commands, but `umbraco commands` tells an agent they are safe reads.

| Command | HTTP | Why it is missed |
|---|---|---|
| `content sort` | PUT `/document/sort` | verb `sort` not in the set |
| `media sort` | PUT `/media/sort` | same |
| `content domains set` | PUT `/document/{id}/domains` | verb `set` not in the set |
| `document-blueprint from-document` | POST `/document-blueprint/from-document` | compound verb not in the set |
| `health run` | POST `/health-check-group/{name}/check` | a read in intent, but POST: blocked by `--readonly`, and `--dry-run` previews it instead of running it |

- **Also local-only writes:** `auth login`, `auth logout` and `auth use` change the config file. They are not API writes, so not flagging them is fine, but conventions.md should say so.
- **Proposed rule:** a leaf declares its safety class explicitly, next to `.Destructive()`, for example `.Mutating()` or `.ReadOnly()`, and the verb set is dropped. Alternatively, keep the set but add a unit test asserting that every leaf sending a non-GET is `mutating`.
  - Decide `health run` explicitly: either mark it mutating, or exempt that URL in `MutationInterceptorHandler`.
- **Change list:** the 5 commands above, plus `CommandCatalog.MutatingVerbs`.
- Only the catalog output changes, so this is not breaking for arguments. Touches ADR 0007 and #84/#255. Effort: S.

### I4. Destructive gating of similar high-impact writes

The dominant rule in the code comments is "gate = irreversible, or takes live content offline". These do not follow it:

- **Ungated, though they wipe state:**
  - `content update --replace`: absent values are cleared.
  - `document-blueprint update --replace`: same.
  - `content domains set --replace`: drops bindings, which can make a culture unreachable.
  - `user-groups update`: omitted options are reset to their defaults (see I17).
  - `user-groups remove-users`.
- **Gated, though reversible:**
  - `models-builder build` regenerates generated files.
  - `redirect tracking disable` is gated while `enable` is not. That one is defensible, but it is the only feature toggle gated.
  - `indexer rebuild` is gated for cost, not for loss. Defensible, but it is a different criterion.
- **Proposed rule, for conventions.md:** "Gate when the command can cause data loss that the CLI cannot undo, or takes live content or a site feature offline. Cost alone does not gate."
  - Under that rule: add `destructiveWhen: --replace` to the three `--replace` commands; gate `user-groups update` or fix I17 instead; decide on `models-builder build` and `indexer rebuild`.
- Breaking, because it adds `--yes` requirements. Touches #82/#255. Effort: S.

### I5. Cascading deletes without the `--force` guard

- `--force` exists on 4 type deletes only.
- These deletes also cascade or orphan data, but have no guard:
  - `templates delete` (templates in use by document types).
  - `languages delete` (deletes that culture's variants and translations).
  - `member-groups delete` (group membership).
  - `user-groups delete` and `delete-many` (group assignments).
  - `dictionary delete` (child items).
  - `data-types folder delete` and `document-blueprint folder delete` (non-empty folders; the server may refuse these, so confirm).
- **Proposed rule:** "A delete that removes or orphans *other* entities is refused unless `--force` is given, and the check runs before the prompt."
- Breaking. Touches #246/#253. Effort: M, because each needs a server-side check.

### I6. The result shape of writes

Measured from the `executor.RunXxxAsync` call sites.

| Verb | Returns the entity (`data`) | Returns only a message (`{status, message, meta}`, no `data`) |
|---|---|---|
| create (21) | 20 | **`users invite`** |
| update (17) | `content`, `languages`, `members`, `dictionary` (4) | `content-types`, `data-types`, `templates`, `member-types`, `member-groups`, `user-groups`, `user-data`, `document-blueprint`, `data-types folder`, `document-blueprint folder`, `script`, `stylesheet`, `partial-view` (13) |
| other writes | `content copy`, `data-types copy`, `content domains set`, `content publish-descendants`, `media upload`, `document-blueprint from-document` | `move` ×5, `content publish`, `content unpublish`, `content rollback`, `content trash`, `content restore`, `media trash`, `media restore`, `content sort`, `media sort`, `user-groups add-users`, `user-groups remove-users`, `indexer rebuild`, `models-builder build`, `redirect tracking enable`, `redirect tracking disable` |
| delete | none | all 23, plus `delete-many` and `empty-recycle-bin` ×2 |

- **55 commands use the message-only envelope.** It is not documented: agent-guide §3 says the payload always lives under `data`. Under `--quiet` these commands print nothing to stdout.
- **Proposed rule:** "Every success has `data`." Writes return the resulting entity, hydrated. Deletes and actions return `{ "id": ... }` or `{ "ids": [...] }`. The human-readable message becomes a human-output concern only.
- Breaking: envelope schemaVersion 6. Effort: M.

### I7. Lists and paging

- **Default `--take` is split:**
  - **20** on 19 commands: `content list`, `content find`, `content versions`, `media list`, `media find`, `media-types list`, `content-types list`, `data-types list`, `templates list`, `members list`, `member-types list`, `users list`, `dictionary list`, `webhooks list`, `script list`, `stylesheet list`, `partial-view list`, `member-groups list`, `searcher query`.
  - **100** on 17 commands: `data-types referenced-by`, `dictionary tree`, `tags list`, `cultures list`, `user-groups list`, `user-data list`, `document-blueprint list`, `health list`, `log-viewer log`, `log-viewer levels`, `log-viewer message-templates`, `log-viewer saved-search list`, `redirect list`, `relation-type list`, `relation list`, `indexer list`, `searcher list`.
  - `commands.md` says "default 20" for all of them.
- **Hand-rolled paging:** `content list` and `members list` declare `--skip`/`--take` themselves instead of using `PagingOptions`. As a result, 35 of 36 paged commands show `--skip`/`--take` with no help text; only `content list` has it.
- **Wrong output shape:** `data-types referenced-by` takes `--skip`/`--take` but renders through `RunObjectAsync`, so it has no paging `meta`.
- **Lists with no paging** (complete lists or walks; acceptable, but list them in conventions.md): `languages list`, `manifest list`, `server troubleshooting`, `auth profiles`, `content tree`, `media tree` (these two use `--depth`), `imaging resize-urls`.
- **Proposed rule:** one default in `PagingOptions` (see D4); every paged command uses `PagingOptions`; the options carry help text.
- Breaking (the default changes). Effort: S.

### I8. Verb vocabulary

| Pattern | Dominant | Outliers | Proposal |
|---|---|---|---|
| Many-at-once | sub-noun `content bulk delete\|publish\|unpublish` (3), which reads ids from stdin or a file and reports per item | `user-groups delete-many --ids` | `user-groups delete <id>...` (variadic positional), or `user-groups bulk delete`. See D6 |
| Membership | none | `user-groups add-users --user`, `user-groups remove-users --user` (the only verb-noun compounds) | `user-groups user add\|remove <group> --user`, or keep. Low priority |
| Collection vs item | `list` / `get` | `content versions` (list) and `content version` (get), a plural/singular verb pair | sub-noun `content version list <id>`, `content version get <version-id>`, `content version rollback <version-id>` (like `content domains get\|set`) |
| `tree` | `content tree` and `media tree` are a recursive walk (no paging, `--depth`) | `dictionary tree` is **one level, paged**, which is what `content list --parent` does; `dictionary list` is a flat list of every item | `dictionary list [--parent]` for one level; `dictionary tree` for the recursive walk |
| Read a status | `get` (23) | `redirect status` reads the *tracking* state, but the tracking toggles live under `redirect tracking` | `redirect tracking status` |
| Stutter | none | `log-viewer log` | `log-viewer list`, or rename the noun (`log list`) |
| Noun used as a verb | `auth login\|logout\|whoami\|doctor` | `auth profiles`, `auth use` | `auth profile list\|use` (gh has `auth switch`; az has `account list\|set`). Low priority |
| Domain verbs for create | `create` (21) | `media upload`, `users invite`, `document-blueprint from-document` | Keep `upload` and `invite` (real Umbraco actions). Fold `from-document` into `document-blueprint create --from-document <id>` so it lands in the `create` safety class |
| Help-text verb disagrees with the command verb | the help text says the verb | `languages create` says "Add a language."; `languages delete` says "Remove ..."; the folder `update` commands and `member-groups update` say "Rename ..." | Match the verb, or rename the folder `update` to `rename` if that is the intent |

- Breaking for every renamed command. Effort: S each, M together.

### I9. Sub-noun vs flat structure

- Folders are inconsistent:
  - `data-types folder` and `document-blueprint folder` have get/create/update/delete and no list; listing goes through the parent noun's `list`.
  - `media folder` has create only. That is justified (a media folder is a media item), but conventions.md should state the rule.
- **`relation`** is its own top-level noun with one verb (`list --type <relation-type-id>`), next to `relation-type`. Candidate: `relation-type relations <id>`, or keep both but make `--type` accept an alias.
- **`property-type`** is a top-level noun with one verb, `is-used`. Compare `data-types is-used`, which hangs off its type.
- **Proposed rule:** "A sub-noun groups verbs about a sub-resource of one parent (`domains`, `folder`, `tracking`, `version`). A top-level noun needs at least a `list` or a `get`."
- Effort: S. Mostly a convention decision.

### I10. CRUD coverage matrix (list / get / create / update / delete)

| Noun | L | G | C | U | D | Other verbs |
|---|---|---|---|---|---|---|
| content | Y | Y | Y | Y | Y | tree, find, publish, unpublish, versions, version, rollback, trash, restore, empty-recycle-bin, move, sort, copy, publish-descendants, export, diff, apply |
| content domains | - | Y | - | - | - | set |
| media | Y | Y | (upload) | **-** | Y | tree, find, trash, restore, empty-recycle-bin, move, sort |
| media folder | - | - | Y | - | - | |
| media-types | Y | Y | Y | **-** | Y | |
| content-types | Y | Y | Y | Y | Y | |
| data-types | Y | Y | Y | Y | Y | is-used, referenced-by, copy, move |
| data-types folder | - | Y | Y | Y | Y | |
| languages | Y | **-** | Y | Y | Y | |
| templates | Y | Y | Y | Y | Y | |
| members | Y | Y | Y | Y | Y | |
| member-types | Y | Y | Y | Y | Y | |
| users | Y | Y | (invite) | **-** | **-** | |
| dictionary | Y | Y | Y | Y | Y | tree, move |
| webhooks | Y | **-** | Y | **-** | Y | |
| script / stylesheet / partial-view | Y | Y | Y | Y | Y | |
| member-groups | Y | Y | Y | Y | Y | |
| user-groups | Y | Y | Y | Y | Y | delete-many, add-users, remove-users |
| user-data | Y | Y | Y | Y | Y | |
| document-blueprint | Y | Y | Y | Y | Y | scaffold, from-document, move |
| document-blueprint folder | - | Y | Y | Y | Y | |
| log-viewer saved-search | Y | **-** | Y | - | Y | |
| redirect | Y | **-** | - | - | Y | status |
| relation-type | Y | Y | - | - | - | |
| relation | Y | - | - | - | - | |
| health | Y | Y | - | - | - | run |
| indexer | Y | Y | - | - | - | rebuild |
| searcher | Y | **-** | - | - | - | query |
| tags, cultures, manifest | Y | - | - | - | - | |

Obvious gaps (bold): `media update` (rename or set values), `media-types update` (while `content-types` and `member-types` have one), `languages get <iso-code>`, `webhooks get` and `webhooks update`, `users update` and `users delete` (or disable), `redirect get`, `log-viewer saved-search get`, `searcher get`. Not breaking. Effort: S–M each.

### I11. Identifier arguments

| Issue | Commands | Proposal |
|---|---|---|
| The same accepted forms under a different argument name | `templates get <alias>` vs `templates update\|delete <id>` (all three accept id, alias or name) | `templates get <id>` |
| camelCase argument | `document-blueprint from-document <documentId>` | `<document-id>` (or fold into create, I8) |
| Argument named `key` | `dictionary get\|update\|move\|delete <key>` (a string: id or key); `user-data get\|update\|delete <key>` (a GUID) | Name every entity selector `id`, as the other 25 string `id` arguments already do while accepting names. The help text explains "or its key". |
| A positional duplicated by an option | `user-data update <key?> --key <guid>` | Drop `--key`; make the positional required |
| `--key` used for the idempotent id | `user-data create --key` (17 other creates use `--id`) | `--id` |
| No idempotent `--id` | `users invite` | Add `--id` if the API honours it (check) |
| One value, two names | `languages create --culture <iso>` vs `languages update\|delete <iso-code>` | `<culture>` everywhere (or `iso-code` everywhere) |
| Positional optional only because of `--schema` | `content update <id?>`, `content-types update <id?>`, `data-types update <id?>`, `document-blueprint update <id?>` | Accept; document that `--schema` makes the argument optional. Or make `--schema` its own verb (D5) and the argument becomes required again |
| Option-shaped identifiers | `imaging resize-urls --id <guid[]>`, `property-type is-used --content-type --alias`, `relation list --type <guid>`, `user-groups delete-many --ids` | `imaging resize-urls <id>...` and `user-groups delete <id>...` as variadic positionals; the other two are filters and can stay |

Lookup forms differ per noun (from the help texts):

| Target | Accepts |
|---|---|
| `media-types`, `templates`, `user-groups` | id, alias, name |
| `content-types`, `member-types` | id, alias |
| `data-types`, `member-groups` | id, name (they have no alias) |
| `dictionary` | id, key |
| `content`, `media`, `members`, `users`, `webhooks`, `redirect`, `relation-type`, `document-blueprint`, folders | GUID only |

- Missing natural keys: `members` (email/username), `users` (email/username), `webhooks` (name).
- **Proposed rule:** "Selectors are the positional `<id>`. They accept the GUID plus every natural key the entity has, resolved alias → name, ignoring case (document types: alias only)."
- Breaking for the renames. Effort: S (renames); M for new lookups.

### I12. Type-reference option names

| Option | Command | Accepts (per help) |
|---|---|---|
| `--content-type` | `content create` | alias |
| `--content-type` | `property-type is-used` | id or alias |
| `--document-type` | `document-blueprint create` | alias or UUID |
| `--media-type` | `media upload` | id, alias or name |
| `--type` | `members create` | alias |

- **Proposed rule:** `--<kind>-type`, using the Umbraco kind name. Every such option accepts id|alias (and name where the resolver allows it).
- **Change list:**
  - `members create --type` → `--member-type`.
  - `content create --content-type` → `--document-type` (per D2).
  - `property-type is-used --content-type` → `--type` or `--document-type` (the API's `contentTypeId` covers any kind; check before renaming).
- Breaking. Effort: S.

### I13. One option name, several meanings

| Option | Meaning A | Meaning B | Proposal |
|---|---|---|---|
| `--content` | inline file body (8: `script\|stylesheet\|partial-view create\|update`, `templates create\|update`) | **document GUID filter** (`redirect list`) | `redirect list --document <id>` |
| `--key` | dictionary item name (`dictionary create`, `dictionary update`) | idempotent GUID (`user-data create`) and entry selector (`user-data update`) | dictionary: keep, since "key" is Umbraco's term; user-data: see I11 |
| `--type` | member type alias (`members create`) | relation type GUID (`relation list`) | `--member-type` (I12); `relation list --relation-type` |
| `--default` | flag (`languages create`, `languages update`) | ISO code string (`content domains set`) | `content domains set --default-culture <iso>` |
| `--value` | `alias=value` property pairs, repeatable (`media upload`, `members update`) | the raw stored value, single (`user-data create`, `user-data update`) | keep `--value` for `alias=value`; rename user-data's to `--data`, or accept that it is the entity's own field name |
| `--group` | string filter (`members list`, `tags list`, `user-data list`) | string[] setter (`members update`, `users invite`); a single field (`user-data create`, `user-data update`) | same word, different arity inside the `members` noun; accept, but give `members list --group` help text |
| `--name` | the value to set (32) | search text (`content find`, `media find`) | accept (gh `--search`, az `--query` are alternatives); low priority |

Breaking for renamed options. Effort: S.

### I14. Multi-value option names: singular vs plural → maintainer decision (D3)

| Form | Names (occurrences) |
|---|---|
| Singular, repeatable (11 names / 17 uses) | `--domain`, `--exclude-root`, `--exclude-type`, `--fallback-permission` ×2, `--group` ×2, `--id` (imaging), `--language` ×2, `--level`, `--section` ×2, `--user` ×2, `--value` ×2 |
| Plural (5 names / 11 uses) | `--cultures` ×5 (`content publish`, `content unpublish`, `content publish-descendants`, `content bulk publish`, `content bulk unpublish`), `--children` ×2 (`content sort`, `media sort`), `--events` (`webhooks create`), `--ids` (`user-groups delete-many`), `--values` ×2 (`dictionary create`, `dictionary update`) |

### I15. The ISO culture code under three names

- **`--culture`** is single-valued on 7 commands: `content create`, `content versions`, `content rollback`, `languages create`, `tags list`, `document-blueprint create`, `document-blueprint update`.
- **`--cultures`** is multi-valued on 5 commands (listed in I14).
- **`--language`** (multi-valued) on `user-groups create` and `user-groups update`; its values are ISO culture codes.
- **The positional `<iso-code>`** on `languages update` and `languages delete`.
- **Proposed rule:** "An ISO culture code is always `--culture`. When several are allowed, `--culture` is repeatable."
- **Change list:** the 5 `--cultures` commands and the 2 `--language` commands; `languages <iso-code>` → `<culture>`.
- Breaking. Effort: S.

### I16. Boolean flags

- **Update flags with the wrong polarity model:**
  - `languages update --default|--mandatory` and `members update --approved` are tri-state (omitted = unchanged; `--x false` clears).
  - `user-groups update --has-access-to-all-languages|--document-root-access|--media-root-access` default to false, so **omitting one resets it**.
  - Rule: "On `update`, an omitted flag leaves the value unchanged."
- **Negative flag:** `content apply --no-state` is the only `--no-*` option. Acceptable per clig.dev (it turns off a default behaviour); consider `--no-publish` for clarity. Low priority.
- **Sort direction:** `content sort --desc` and `media sort --desc` vs `log-viewer log --ascending`. Candidate: `--order asc|desc` on all three.
- **Restore semantics:**
  - `content restore`: omitted `--parent` = the original parent, plus a `--to-root` flag.
  - `media restore`: omitted `--parent` = **the media root**, and no `--to-root`.
  - Align both on "original parent by default, `--to-root` to override".
- Breaking. Effort: S.

### I17. Update semantics: merge vs full replace

- **Dominant: merge.** Omitted fields are preserved. This holds for `content`, `document-blueprint`, `data-types`, `templates`, `member-types`, `languages`, `members` and `dictionary` update (8+). A `--replace` opt-in exists on 3 commands (`content update`, `document-blueprint update`, `content domains set`).
- **Outliers:**
  - **`user-groups update`** requires `--alias` and `--name`, and every option it does not receive is overwritten. Its help text says "pass the full desired state".
  - **`user-data update`** requires `--group`, `--identifier` and `--value`.
- **Proposed rule:** "`update` merges (read-modify-write). `--replace` opts in to full replacement and is `destructiveWhen`."
- Breaking. Effort: M for `user-groups` (read-modify-write); S for `user-data`.

### I18. File and stdin inputs

| Input | Type | Stdin |
|---|---|---|
| `--json-body` (8) | `string` | explicit `-` |
| snapshot positional (4) | `string` | explicit `-` |
| `--content-file` (8) | `file` | **not supported** |
| `content bulk * --file` (3) | `file` | **implicit when omitted** |
| `media upload <file>` | `file` | n/a |

- **Help text contradicts the code:**
  - On all 6 `script`, `stylesheet` and `partial-view` create/update commands, the `--content` help says "Mutually exclusive with --content-file", while `--content-file` says "takes precedence over --content". `FileContentInput.ReadAsync` implements precedence.
  - `--json-body` "overrides other flags" on `content create` and `document-blueprint create`, but on `content-types create` and `data-types create` `--id` "must match" the body.
- **Proposed rule:**
  - "`-` means stdin, explicitly, on every file-valued input." Bulk may keep implicit stdin when stdin is redirected.
  - "Conflicting inputs are an `invalid_argument` error, never a silent precedence."
- Breaking only for callers who relied on precedence. Effort: S.

### I19. `--json-body` / `--schema` coverage and meaning

- **Has J (8):** `content create`, `content update`, `content-types create`, `content-types update`, `data-types create`, `data-types update`, `document-blueprint create`, `document-blueprint update`.
- **No J, although the flags cannot express the full body:** `media-types create` (no update at all), `member-types create` and `member-types update` (properties cannot be set). Less pressing: `members create`/`update`, `user-groups create`/`update`, `webhooks create`.
- **`--schema` means two different things:**
  - On `content create|update` and `document-blueprint create|update` it prints a **JSON Schema**, offline.
  - On `content-types create|update` and `data-types create|update` it prints **an existing entity from the instance** as an example, and needs a host.
- See D5. Breaking. Effort: M (J for media-types and member-types); S (flag split).

### I20. Required-ness and defaults

- Update commands that require fields: `user-groups update --alias! --name!` and `user-data update --group! --identifier! --value!` (I17).
  - The rename-only updates (`data-types folder`, `document-blueprint folder`, `member-groups` `update --name!`) are fine.
- `--icon` has a default on `media-types create`, `content-types create` and `member-types create`, but none on `user-groups create`.
- `--name` or `--alias` are not required on `content-types create` and `data-types create`, only because `--json-body` is an alternative. The catalog explains this via `acceptsJsonBody`, but `media-types create` and `member-types create` require both. That is a consequence of I19 and is resolved with it.
- Effort: S.

### I21. Help text

- **No description on 100 elements:**
  - 35 `--skip` + 35 `--take` (I7).
  - 6 arguments: `media get id`, `media delete id`, `members get id`, `members delete id`, `users get id`, `webhooks delete id`.
  - 24 options:
    - `media-types create` and `content-types create`: `--name`, `--alias`, `--description`, `--is-element`, `--allow-at-root` (10).
    - `member-types create`: `--name`, `--alias`, `--description` (3).
    - `users invite`: `--email`, `--name`, `--message` (3).
    - `templates create`: `--name`, `--alias` (2).
    - `members create`: `--email`, `--name` (2).
    - One each: `data-types create --name`, `members list --group`, `dictionary create --key`, `webhooks create --url`.
- **First line over 80 characters (29 of 225):**
  - `commands` 179, `content versions` 162, `document-blueprint update` 150, `content-types update` 134, `user-groups update` 118, `content-types get` 116, `content apply` 103.
  - `auth logout` 96, `document-blueprint scaffold` 95, `member-types update` 94, `partial-view create` 94, `stylesheet create` 92, `data-types update` 90, `dictionary tree` 90.
  - `auth doctor` 89, `content version` 89, `content sort` 89, `content bulk` 88, `script create` 88, `media sort` 87, `partial-view list` 86, `document-blueprint list` 86, `data-types referenced-by` 85, `schema apply` 85, `stylesheet list` 84, `content tree` 83, `content list` 81, `content unpublish` 81, `media tree` 81.
- **No examples in 79 of 183 leaves:**
  - The whole of `script`, `stylesheet`, `partial-view`, `server`, `health`, `log-viewer`, `models-builder`, `redirect`, `relation*`, `indexer`, `imaging` and `property-type`.
  - Most of `member-groups`, `user-data`, `document-blueprint` and `data-types` (advanced and folder).
  - Also: `auth profiles`, `auth use`, `webhooks create`, `tags list`, `cultures list`, `user-groups list`, `user-groups update`, `user-groups delete`, `searcher list`, `manifest list`, `commands`.
  - The heading varies: "Example:" ×59, "Examples:" ×81.
- **Term drift:**
  - "UUID" 58 / "ID" 48 / "id" 85 / "GUID" 2. Pick "id" in prose and `guid` as the type name.
  - "backoffice"/"Backoffice" 9 vs "back-office" 5. Umbraco spells it "backoffice".
  - "Get" vs "Show" as the first word of a `get` description: 21 vs 2 (`content domains get`, `health get`).
  - Repeatable-option suffix: "Repeat the option, or separate values with commas." ×23, "Repeatable." ×3 (`content domains set --domain`, `media upload --value`, `members update --value`), bespoke ×2 (`dictionary --values`).
  - Argument help has two styles: "Content item ID." (content, blueprint, folders) vs "The media type's id, or its alias or name." (types and groups).
  - Noun descriptions have three templates: "List, inspect, and manage Umbraco X." ×15, "Manage ..." ×8, bespoke ×12.
- **Issue numbers leak into help text:** `(#86)` ×17 (every `--id` on a create), `(#90)` ×3, `(#110)`, `(#181)`.
- **Proposed rules:**
  - The summary is at most 80 characters, imperative, and has no issue references.
  - Every option and argument has help text.
  - Every leaf has an "Examples:" block.
  - One term per concept (enforced by a unit test over the catalog).
- Not breaking. Effort: M (volume), and the whole list can be checked automatically.

### I22. Output, errors and exit codes (from source)

- **No exit-code registry.** The codes are literals:
  - `return 2` ×14, `return 130` ×5, `return 1` ×3 and `return 0` ×4, spread over `CommandExecutor`, `LoginCommand` and `ParseErrorReporter`.
  - Also `BulkSummary.ExitCode` and 8 `WriteError(<literal>, ...)` sites.
- **Exit 1 is overloaded.** It covers:
  - API failure.
  - A parse error (`invalid_argument`).
  - The backstop exception (a malformed `--json-body` or a missing file).
  - `auth doctor` hard fails.
  - A bulk run where some items failed.
- **`category` is only set for API failures and parse errors.** None of these carry one:
  - All exit-2 errors: no host, not authenticated, auth failed, unknown profile, allow-list block, confirmation required, cancelled, `--readonly` block, in-use refusal.
  - The exit-1 backstop.
- **`meta.command` is missing** on 7 abort sites: no profile, allow-list, no host, not authenticated, auth failed, the non-interactive `--yes` refusal (`CommandExecutor.cs:111`), and the auth login/use errors.
- **Five success envelope shapes:**
  - `data` (object or list).
  - `message` with no `data` (55 commands, undocumented).
  - `dry-run`: `meta` carries **only** `schemaVersion`, with no `command`, `timestamp` or `durationMs`.
  - bulk: `status` is `partial` or `error`, plus `meta.summary`.
  - report (diff/apply).
- **CSV errors** print `exitCode,httpStatus,message`. agent-guide §4 still says `code,message`.
- **`meta.command` is hand-typed** as a string literal in about 150 call sites, rather than derived from the parse tree (only `ParseErrorReporter` derives it). This is a drift risk, and it will bite during the noun renames (I1).
- **Proposed rules:**
  - One `ExitCode` enum and one `ErrorCategory` set.
  - Every error carries a `category`: add `not_authenticated`, `not_allowed`, `confirmation_required`, `readonly`, `refused`, `cancelled`, `invalid_input` (post-parse), and `internal`.
  - Every envelope has the full `meta`.
  - `meta.command` is derived from the command tree.
  - See D7 for the numbers.
- Breaking (envelope). Effort: M.

### I23. Docs drift

The script found that every one of the 183 leaves appears as `umbraco <path>` in `commands.md`, and that no documented option is unknown. The remaining drift is narrative:

1. `commands.md` "Paging applies to every `list`": "**default 20**". Wrong for the 17 commands listed in I7.
2. The `commands.md` global-options table omits the aliases `-H` (`--host`) and `-v` (`--verbose`).
3. The `agent-guide.md` §8 row "Any `list` ... Truncates at `--take` (default 20) with no `total` or `hasMore` (#173)" contradicts §3, which says lists now carry `total`/`hasMore`.
4. `agent-guide.md` §4: CSV errors are "a `code,message` line". They are actually `exitCode,httpStatus,message`.
5. `agent-guide.md` §3 says every success payload lives under `data`, but 55 commands emit `message` only (I6).
6. The `agent-guide.md` §9 and `--readonly` help text say it blocks "create/update/delete/publish". It actually blocks every non-GET, including `sort`, `domains set`, `from-document` and `health run`.
7. The `--yes` help text says "destructive commands (delete)". It also covers `unpublish`, `empty-recycle-bin`, `indexer rebuild`, `models-builder build`, `redirect tracking disable` and `apply --prune`.
8. `glossary.md` still defines "Hand-written path". `CONTEXT.md` still defines "Transitional hybrid", although the hand-written HTTP path was deleted in the Kiota migration.
9. `glossary.md`/`CONTEXT.md` have no entries for "document vs content", "key" (dictionary name vs user-data GUID) or "selector". These are needed once D1–D3 are settled.

Not breaking. Effort: S.

---

## Decisions needed

### D1. Noun number: singular or plural?

| Option | Commands changed | Notes |
|---|---|---|
| **A. Singular everywhere** (`media-type`, `user`, `template`, `language`...) | 13 nouns / 62 leaves | Matches the Management API segments, `gh` (`gh pr`, `gh issue`, `gh repo`), `az` (`az vm`, `az group`) and `dotnet` (`dotnet tool`). Sub-noun `domains` → `domain` (2 more). |
| B. Plural everywhere (`scripts`, `redirects`, `indexers`, `relation-types`...) | 11 nouns / 42 leaves | Fewer changes. Reads well with `list`. The mass nouns (`content`, `media`, `dictionary`, `health`) stay as they are either way. |
| C. Keep both and accept the other as a command alias | 0 breaking | System.CommandLine supports aliases, but the catalog, `meta.command` and the allow-list need one canonical name each. |

**Recommendation: A**, with hidden plural aliases for one release. The singular side already mirrors Umbraco's API names, and `gh`/`az` are the closest prior art for a noun-verb CLI. Whichever option is chosen, allow-list entries must be migrated or accepted in both forms (ADR 0007).

### D2. "content" vs "document"

| Option | Changes | Notes |
|---|---|---|
| **A. Keep `content`; rename the types to `document-type`** (plus `--content-type` → `--document-type`) | 1 noun (5 leaves) + 2 options | "Content" is the backoffice section; "Document Type" is what both the UI and the API call the type. `document-blueprint` stays and becomes consistent with it. |
| B. Rename `content` → `document` too | +27 leaves; also `meta.command`, the allow-list and every doc | Full API parity, but it breaks the most-used noun. |
| C. Keep everything; rename `document-blueprint` → `content-blueprint` | 1 noun (12 leaves) | Consistent with "content", but departs from both the UI and the API for the types. |

**Recommendation: A.** It matches what Umbraco users see in the UI and costs little.

### D3. Multi-value options: singular (repeatable) or plural?

| Option | Changes |
|---|---|
| **A. Singular, repeatable** (`--culture`, `--child`/`--order`, `--event`, `--id`, `--value`) | 5 names on 11 commands (`--cultures` ×5, `--children` ×2, `--events`, `--ids`, `--values` ×2) |
| B. Plural for lists (`--sections`, `--users`, `--groups`, `--domains`...) | 11 names on 17 commands, and it collides with the existing single-valued `--culture` |

**Recommendation: A.** It is already the majority. It is how `gh --label`, `docker -e/--env` and `kubectl --selector` work, and it resolves I15 (one `--culture` that may repeat). `--children` has no good singular; `content sort --order <id>...` reads better.

### D4. Default `--take`

| Option | Commands changed |
|---|---|
| A. 20 everywhere | 17 |
| **B. 100 everywhere** | 19 (plus the docs line that already says 20) |
| C. No default: return everything, page only when asked | 36; also contradicts the deliberate "no silent cap" and `--all` stance (#196) |

**Recommendation: B.** `meta.hasMore` is now reliable, so a bigger first page saves agents round trips without hiding anything. One constant in `PagingOptions`.

### D5. `--schema` and the example-body flag

| Option | Change |
|---|---|
| **A. `--schema` = JSON Schema (offline) on all 8. Add `--example` (live entity, needs a host) where the schema is too weak** (`content-types`, `data-types`) | 4 commands change meaning |
| B. Keep one flag name with two meanings, and document it | 0 |
| C. Move both to verbs: `umbraco <noun> schema create`, `umbraco <noun> example` | 8 commands; removes the optional-positional quirk on the 4 `update` commands (I11) |

**Recommendation: A.** One flag, one meaning. The current live-entity behaviour survives under an honest name.

### D6. Bulk operations: sub-noun, variadic positionals, or compound verbs?

| Option | Changes |
|---|---|
| **A. Variadic positionals for "several ids known up front"** (`user-groups delete <id>...`, `imaging resize-urls <id>...`); keep `content bulk` for stdin/file with per-item results | `delete-many` removed; `resize-urls --id` becomes positional (2 commands) |
| B. `bulk` sub-noun everywhere | `user-groups bulk delete` (1), and future nouns follow |
| C. Keep `delete-many` as the pattern | 3 `content bulk` commands renamed to `delete-many`/`publish-many`/`unpublish-many` |

**Recommendation: A.** It is kubectl and gh style (`kubectl delete pod a b`), and it keeps `bulk` for the case that has a genuinely different contract (a per-item envelope, and stdin).

### D7. Exit codes

| Option | Change |
|---|---|
| **A. Keep 0/1/2/130; add a category to every error; one `ExitCode` enum** | No script breaks. The envelope gains categories. |
| B. Move to sysexits-style codes (2 = usage/parse, 3 = policy abort, 4 = auth, 5 = not found...) | Every script gating on `2` breaks; parse errors move from 1 to 2 |
| C. A: plus parse errors move to 2 (the common "usage" convention), policy aborts stay 2 | Small break (parse errors only) |

**Recommendation: A.** The category already carries the distinction that B would encode in numbers, and agent-guide tells callers to gate on the exit code and then read the envelope.

### D8. What `destructive` means (I4)

Decide between "irreversible or takes live things offline; cost alone does not gate" (the recommendation) and "anything expensive or impactful". Under the first:

- `--replace` becomes `destructiveWhen` on 3 commands.
- `models-builder build` is ungated.
- `indexer rebuild` is judged by whether a rebuild can lose data. It cannot, so ungate it.

This changes `--yes` requirements on 5 commands.

---

## Gaps in what the snapshot captures

`surface.json` cannot currently prove the conventions above. Candidate fields and tests:

1. **Default values.** It records `hasDefault` but not the value. It could not show the 20/100 split in `--take`, or the defaults for `--icon`, `--media-type` and `--scope`. Add `default`.
2. **Output shape per command.** Nothing records whether a command renders object, paged list, complete list, message, bulk or report. I6 and I7 came from source. Add `output: "object|list|pagedList|message|bulk|report"`.
3. **Exit codes and error categories.** These are global and live in no registry. Add a root-level `exitCodes` and `errorCategories` block, generated from an enum.
4. **The HTTP method or methods a command sends.** Without them, the snapshot cannot flag the `mutating` misclassifications (I3). A test could compare `mutating` against the declared HTTP verbs.
5. **Accepted identifier forms** (GUID / alias / name / key / path). Today this is only in help prose (I11, I12). Add `accepts: ["id","alias","name"]` on selector arguments and type-reference options.
6. **Mutually exclusive and dependent options** (`--content` vs `--content-file`, `--to-root` vs `--parent`, `--by` vs `--children`, "`--depth` overrides `--recursive`", "`--exclude-*` only with `--prune`"). These are only in prose, which is how I18's contradiction slipped through.
7. **Pre-flight refusals.** `RefuseWhen` checks (the in-use `--force` guard) are not reported. Add `refusesWithout: "--force"`, parallel to `destructiveWhen`.
8. **Stdin support per input** (`-` allowed, implicit stdin).
9. **Hidden aliases and deprecations.** Not needed yet, but D1 option A's hidden plural aliases will need a `deprecatedAliases` field so the catalog stays canonical.
10. **Command aliases** (System.CommandLine `Command.Aliases`) are not emitted at all today.
11. **`meta.command` value per command.** It is hand-typed in source and could differ from the path. A snapshot test asserting `meta.command == dotted path` for every leaf would pin it down before the renames.

---

## Decisions taken (2026-09-25)

The maintainer accepted the recommendation on all eight. These are the inputs to `docs/conventions.md` and the batch.

| # | Decision |
|---|---|
| D1 | **Singular nouns** everywhere (`media-type`, `user`, `template`, `language`, sub-noun `domain`...). Hidden plural aliases for one release; allow-list entries migrated or accepted in both forms (ADR 0007). |
| D2 | **Keep `content`; types are `document-type`.** `content-types` becomes `document-type`, `--content-type` becomes `--document-type`. `document-blueprint` stays. |
| D3 | **Multi-value options are singular and repeatable**: `--culture`, `--event`, `--id`, `--value`; `content/media sort --children` becomes `--order`. |
| D4 | **Default `--take` is 100** on every paged command, from one constant in `PagingOptions`. |
| D5 | **`--schema` means JSON Schema (offline) everywhere**; the live-entity example moves to a new `--example` on `content-types`/`data-types` create/update. |
| D6 | **Variadic positionals** when ids are known up front (`user-groups delete <id>...`, `imaging resize-urls <id>...`); `delete-many` is removed; `content bulk` stays for stdin/file with per-item results. |
| D7 | **Exit codes stay 0/1/2/130.** One `ExitCode` enum; every error carries a `category` (add `not_authenticated`, `not_allowed`, `confirmation_required`, `readonly`, `refused`, `cancelled`, `invalid_input`, `internal`). |
| D8 | **Gate on loss or offline only**: `--replace` becomes `destructiveWhen` on `content update`, `document-blueprint update`, `content domains set`; `models-builder build` and `indexer rebuild` are ungated. |

I3 (writes reported as reads) was fixed separately in d3f7c83.
