# Hands-on test ledger

Every bug, gap and improvement found while running the harness, **written down as it is found**, one round per section.
`tools/file_issues.py` turns unfiled rows into GitHub issues and writes the numbers back here.

Rounds 1–4 (2026-09-23 → 2026-09-28, alpha.6 → alpha.13, findings L-001 → L-102) are recorded in their tracking issues
#187, #250 and #302 (round 4: a comment on #302 and #320). `ledger-history.json` maps those ids to issue numbers, so
`L-0xx` references in new entries still become links. **New findings continue from L-103.**

## Format (the filing script depends on it)

Start each round with this heading and metadata line (copy the values from `round.env`):

```markdown
# Round 5 · 2026-10-05 · CLI 0.1.0-local.20261005.101500 (abc1234) · Umbraco 17.7.0
<!-- round: label=test-round:2026-10-05 cli=0.1.0-local.20261005.101500 commit=abc1234 umbraco=17.7.0 -->
```

`label` becomes a GitHub label on every issue filed from the round. Make it unique per round (add `b`, `c` for a second round on the same day).

Then, in this order:

1. **Re-test table:** one row per issue under test (the previous round's filed issues, plus still-open `cli-testing` issues):
   `| Issue | Ledger | Result | Notes |` with ✅ fixed / 🟡 partly / ❌ still failing and one line of evidence.
2. **Test log:** `| Test | Result | Notes |`, one row per A-step group and T1–T10, ✅ / 🟡 / 🔴, naming the findings it produced.
3. **Findings table**, exactly these 7 columns:
   `| ID | Type | Sev | Labels | Command | Title | Issue |`
   - **Type:** `Bug`, `Gap` or `Improvement`.
   - **Sev:** 🔴 data loss / blocks a headline feature (P0), 🟠 high value (P1), 🟡 nice to have (P2).
   - **Labels:** area labels that exist on the repo, comma-separated: `content`, `media`, `schema`, `members`, `languages`,
     `webhooks`, `auth`, `safety`, `error-handling`, `agent-dx`, `dx`, `content-workflow`, `coverage`, `documentation`,
     `api-consistency`, `infrastructure`.
   - **Command:** the command(s) involved, in backticks. **Title:** one line; escape `|` as `\|`.
   - **Issue:** leave **empty** until filed. Anything in it (`#123`, `covered by #196`, `not a bug`) stops the script filing the row.
4. **One entry per finding** (becomes the issue body):

```markdown
### L-103 · Bug 🟠 · Short title

- **Found:** <date> · CLI <version> · <test, e.g. T1>
- **Repro:** exact commands and their output (trim to the relevant part).
- **Expected:** what should happen, and why (docs, help text, consistency with other commands).
- **Workaround:** if any.
- **Suggestion:** optional.
```

---

# Round 5 · 2026-09-28 · CLI 0.1.0-alpha.14 (f184070) · Umbraco 17.7.0
<!-- round: label=test-round:2026-09-28c cli=0.1.0-alpha.14 commit=f184070 umbraco=17.7.0 -->

Build: local `dotnet pack -p:Version=0.1.0-alpha.14` of `f184070` (`--nupkg`; the NuGet push waits on GitHub Actions billing).
T2-T10 ran as five parallel agent lanes, with a third site (`sites/scratch`) for the user and hand-written snapshot probes.
**Environment:** with five lanes on one SQLite source site, Umbraco hung on `SQLite Error 6: 'database table is locked'` for ~10 min
(19:42-19:52 UTC), and again briefly around 20:44-20:49 UTC. This is Umbraco/SQLite under concurrent load, not the CLI. Scheduled jobs fired late, and T5 was re-run.

## Re-tests

| Issue | Ledger | Result | Notes |
|---|---|---|---|
| #320 | L-102 | ✅ | `content version get` returns `documentType: {id, icon, alias:"blogPost"}` |
| #325 / #344 | | ✅ | `content publish` with no `--culture` returns `cultures:["en-US","da-DK"]`; `--culture da-DK` returns `["da-DK"]`; invariant returns `null`. The `--culture` on an invariant doc is echoed (L-119) |
| #345 | | ✅ | `content unpublish` returns `{id, cultures}`; a random GUID gives 404 `ContentNotFound`, nothing sent. Already-Draft cultures are listed (L-119) |
| #340 | | ✅ | `document-type list` has real aliases in JSON and in the human table. Same bug remains in `document-blueprint list` (L-118) |
| #342 | | ✅ | All 200 leaf commands have examples; the content/redirect/bulk/script/blueprint examples ran as written. One wrong example (L-123) |
| #237 / #332 / #341 | | ✅ | `webhook get/update/log list/event list`, `--enabled`, `--header` merge, `--replace --yes`, `--type` filtering (verified by delivery), `headers` always present. Gaps: L-124, L-125 |
| #214 | L-047 | ✅ | `user create` with groups and a password works with no SMTP, and the user can sign in. A weak password is rolled back cleanly (nothing left in the API or SQLite) |
| #216 | L-049 | 🟡 | `user get` has groups, sections, start nodes, UI language and login data; `update` and `delete` work. Content languages are still missing (L-113) |
| #198 / #338 | | 🟡 | Name references, generated ids, `Tab/Group` paths and partial sections apply correctly, and absent sections are left alone by `--prune`. A hand-written snapshot never re-diffs clean (L-107, L-108) |
| #196 | | 🟡 | `--all` on every paged list, `--all --take` refused, the 10,000 cap is in the help and fails loudly. `log-viewer list --all` repeats rows while the log grows (L-126) |
| #166 | L-009 | 🟡 | `-v` logs request and response bodies to stderr, stdout stays clean. Redaction misses webhook headers (L-106), over-redacts (L-130), and auth logs nothing (L-131) |
| #174 | L-018 | 🟡 | `content create --example` gives editor-shaped values that work with real ids substituted; as printed, the picker placeholders give an Umbraco 500 (L-133) |
| #92 | | 🟡 | bash (Git Bash) and pwsh work; zsh works (zsh 5.9, Linux container) only after stripping CRs from a Windows-built package (L-132, L-136) |
| #84 | | 🟡 | The catalog has `default`, `requiredUnless`, `jsonBodySchema` (all 14 resolve) and `examples`; help text disagrees with `requiredUnless` twice (L-134) |

## Test log

| Test | Result | Notes |
|---|---|---|
| Part A | ✅ | `All steps passed`, no SLOW lines, 8/8 forms |
| T1 | ✅ | schema/media/content re-diffs empty; 49/49 pages identical; 8/8 forms on staging; subtree prune exact, submissions kept; whole-tree prune refused. L-103, L-104 |
| T2 | ✅ | moves 301 (en + da), sort `--order`/`--by`/`--desc`, rename 301s without a chain, trash/restore `--publish`, empty-recycle-bin guard, rollback, find/tree. L-118, L-120, L-121 |
| T3 | ✅ | both events delivered with matching payloads and custom headers; unknown event rejected with a suggestion. L-105, L-106, L-123-L-125 |
| T4 | ✅ | stylesheet/script/partial-view CRUD, folders, `--content-file -`, served files update and 404 after delete, 53/53 staging URLs render. L-122 |
| T5 | ✅ | publish-at fired 9 s after its time, unpublish-at within 30 s (first attempt 10 min late during the SQLite stall, re-run); `publish-descendants --wait` left the scheduled draft alone |
| T6 | ✅ | folders, `create --from-document`, move, merge update, scaffold, then create and publish give 200 in en + da. L-118 |
| T7 | ✅ | indexers Healthy, rebuild, Docker post first, 16 tags, webp 300x200 |
| T8 | ✅ | housekeeping correct; in-use data-type/document-type deletes and a `--prune` snapshot missing `blogYear` all refused; a snapshot with no `documentTypes` section prunes nothing. L-128 |
| T9 | ✅ | every command returns meaningful data; errors in the log are explained by the round (mostly the SQLite stall). L-127, L-129 |
| T10 | ✅ | `safety_matrix.py` 23/23; the right site per profile; auth in an isolated config only touched the named profile. L-130, L-131, L-137 |
| Users (#334) | 🟡 | create/get/update/delete, password rollback, delete refused for a signed-in user, old password stops working, `--document-permission` add and `<id>=` removal (verified in SQLite). L-109-L-113 |
| Partial snapshots (#338) | 🟡 | a hand-written file with names, `Tab/Group` paths, a new data type used by name and a template by alias applies correctly; prune scoping correct; bad names fail before any write. L-107, L-108, L-114-L-117 |
| DX (#336) | 🟡 | `--all`, `-v` bodies, catalog, `--example`, completion. L-126, L-130-L-136 |

## Findings

| ID | Type | Sev | Labels | Command | Title | Issue |
|---|---|---|---|---|---|---|
| L-103 | Bug | 🟡 | content-workflow,schema | `content diff`, `content apply` | content diff: a document type's icon change marks every document of that type Changed (and apply republishes them) | #346 |
| L-104 | Bug | 🟡 | agent-dx,documentation,api-consistency | `--quiet` | --quiet: conventions.md says it drops write results, --help says data is kept; create keeps its result, delete drops its `{id}` | #347 |
| L-105 | Bug | 🟠 | dx | `-o human` on every `get`/`create`/`update` | -o human (the terminal default) prints only "✓ Done" for every command that returns an object: no data, no new id | #348 |
| L-106 | Bug | 🟠 | safety,webhooks | `-v`, `webhook create/get/list/update/log list` | -v redaction misses webhook header values (Authorization, X-Api-Key, Cookie) and other credential-shaped keys, though --help says "Secrets are redacted" | #349 |
| L-107 | Bug | 🟠 | schema | `schema diff`, `schema apply` | schema diff compares containers and properties by array position, so a hand-written snapshot never re-diffs clean | #350 |
| L-108 | Bug | 🟠 | schema,documentation | `schema diff`, `schema apply` | schema diff: fields a hand-written snapshot omits (cleanup, data type isDeletable/canIgnoreStartNodes) show as Changed; the docs' own example re-diffs dirty | #351 |
| L-109 | Bug | 🟡 | safety | `--dry-run`, `member create/update`, `user update`, `webhook create` | --dry-run prints passwords and secret headers in clear on stdout, while -v redacts the same body | #352 |
| L-110 | Improvement | 🟡 | dx,agent-dx | `user create`, `user update` | --dry-run previews only the first request of a multi-step user write | #353 |
| L-111 | Bug | 🟡 | api-consistency,agent-dx | `user-group create` | user-group create: response echoes the request (isDeletable/aliasCanBeChanged false, description null) where get says true/"" | #354 |
| L-112 | Improvement | 🟡 | error-handling | `user delete` | user delete with several ids: the "has logged in" refusal doesn't say which user | #355 |
| L-113 | Improvement | 🟡 | agent-dx | `user get` | user get: no content languages (the union of the groups' languages / all-languages access), unlike sections | #356 |
| L-114 | Bug | 🟡 | schema,error-handling | `schema apply` | schema apply: a bare name in allowedDocumentTypes passes dry-run, then fails mid-apply after earlier creates | #357 |
| L-115 | Bug | 🟡 | api-consistency,schema | `document-type get`, `schema apply` | Document types resolve by alias only, not name (conventions 3.2 and the schema docs say alias then name; media-type does) | #358 |
| L-116 | Improvement | 🟡 | schema | `schema apply` | A snapshot entry's name silently shadows a live type's alias in a reference instead of being reported as ambiguous | #359 |
| L-117 | Bug | 🟡 | documentation,schema | `schema apply --prune` | commands.md says prune refuses every document/media type delete, but since #287 an unused type is pruned without --force | #360 |
| L-118 | Bug | 🟡 | content,api-consistency | `document-blueprint list/get/scaffold`, `content find` | document-blueprint list shows `documentType.alias: ""`; blueprint get/scaffold and content find rows have no alias | #361 |
| L-119 | Bug | 🟡 | content,content-workflow | `content publish`, `content unpublish` | publish/unpublish `cultures` echoes the request: `["en-US"]` for an invariant document, and already-Draft cultures listed as unpublished | #362 |
| L-120 | Improvement | 🟡 | content,error-handling | `content move`, `content copy`, `content sort` | content move/copy/sort: Umbraco's generic 400 (NotAllowed / SortingInvalid) passes through with no hint | #363 |
| L-121 | Gap | 🟡 | content,media,safety,coverage | `content empty-recycle-bin`, `media empty-recycle-bin` | No way to list the recycle bin, so `empty-recycle-bin --yes` deletes items you can't see | #364 |
| L-122 | Gap | 🟡 | coverage | `stylesheet`, `script`, `partial-view` | stylesheet/script/partial-view: no rename for files or folders | #365 |
| L-123 | Bug | 🟡 | documentation,webhooks | `webhook log list --help` | webhook log list help example `jq '.data.items[]'` fails: `data` is an array | #366 |
| L-124 | Gap | 🟡 | webhooks,dx | `webhook update --header` | You can't remove one webhook header: `--header X=` stores an empty header, and `--replace` means retyping every other header, secrets included | #367 |
| L-125 | Improvement | 🟡 | webhooks,error-handling | `webhook create/update --type` | webhook --type: an unknown alias gets no "did you mean" (unlike --event), and a type that can never match the events is accepted silently | #368 |
| L-126 | Bug | 🟡 | agent-dx | `log-viewer list --all` | --all repeats rows at every page boundary while the log grows (newest-first offset paging), and meta.total counts the repeats | #369 |
| L-127 | Improvement | 🟡 | agent-dx | `health run` | health run results carry only each check's id, not its name | #370 |
| L-128 | Improvement | 🟡 | schema,agent-dx | `property-type is-used` | property-type is-used returns false for an alias the document type doesn't have | #371 |
| L-129 | Improvement | 🟡 | error-handling | `user-data get` | user-data get on a missing key says "unexpected HTTP 404 with no error details" | #372 |
| L-130 | Improvement | 🟡 | dx,members | `-v`, `member get/list`, `user list` | -v redacts failedPasswordAttempts / lastPasswordChangeDate (and any non-string value under a matching key), hiding lockout debug info | #373 |
| L-131 | Gap | 🟡 | auth,dx | `-v`, `auth login`, `auth doctor` | -v logs nothing for auth login, auth doctor or the token exchange | #374 |
| L-132 | Bug | 🟡 | dx,infrastructure | `completion zsh`, `completion bash` | completion scripts carry the build checkout's line endings: a Windows-built package emits CRLF, which zsh can't parse and Linux bash can't eval | #375 |
| L-133 | Improvement | 🟡 | content,agent-dx | `content create --example` | content create --example body isn't round-trippable: "<media id>" / "<document id>" placeholders give an Umbraco 500 | #376 |
| L-134 | Bug | 🟡 | documentation,agent-dx | `commands`, `content update`, `document-blueprint create` | help text and catalog requiredUnless disagree (content update --template; blueprint --from-document) | #377 |
| L-135 | Bug | 🟡 | members | `member create` | member create result has createDate 0001-01-01T00:00:00+00:00 | #378 |
| L-136 | Improvement | 🟡 | dx | `completion bash/zsh/pwsh` | completion: no values for -o, pwsh doesn't filter by the typed word, /? /h offered, and scripts call whatever `umbraco` is first on PATH | #379 |
| L-137 | Improvement | 🟡 | auth | `auth login`, `auth logout` | config file persists computed PascalCase properties IsComplete / EffectiveDefault | #380 |
### L-103 · Bug 🟡 · content diff: a document type's icon change marks every document of that type Changed

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T1 (drift)
- **Repro:** on the source, change only the `blogPost` icon (`document-type get blogPost | jq '.data.icon="icon-rss" | .data' | document-type update blogPost --json-body -`), export content, and diff on staging *before* applying the schema:
  ```
  $T content diff content2.json | jq -c '[.data[] | .changes[]?] | group_by(.) | map({k:.[0],n:length})'
  [{"k":"documentType.icon","n":23},{"k":"state[da-DK]","n":21},{"k":"state[en-US]","n":22}]
  ```
  The two snapshots differ only in `body.documentType.icon` on each post (checked with `diff` on one document). `content apply` would update and republish all 23 posts, making a new version of each, for no content change.
- **Expected:** `body.documentType` references the schema; it isn't content. `content diff` should compare the type by id only. A schema change is `schema diff`'s job, and a content diff run before `schema apply` should show only the real content drift.
- **Workaround:** run `schema apply` before `content diff` / `content apply`. After that the diff is exact.
- **Suggestion:** drop `documentType.icon` / `documentType.collection` in `ContentBodyNormaliser.ForComparison` (and probably from the snapshot body).

### L-104 · Bug 🟡 · --quiet: three descriptions of what it drops, and the behaviour matches none of them

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T1 (side check)
- **Repro:**
  ```
  $U dictionary create --key r5z-q --value en-US=x -q     # prints the full envelope with the created item
  $U dictionary delete r5z-q --yes -q                       # prints nothing, exit 0 (the {"id": ...} result is dropped)
  $U schema export --out x.json -q                          # prints the full envelope
  ```
- **Expected:** one contract, stated once. It currently reads three ways:
  - `docs/conventions.md` 6.2: "`--quiet` drops write results along with the confirmations" (so `create -q` should print nothing).
  - `--help`: "Suppress success confirmations (e.g. "Deleted."). Requested data, errors, and exit codes are unaffected." (so `delete -q` should still print `{ "id": ... }`, which 6.2 calls its result).
  - `QuietOutputWriter`: suppresses only `WriteMessage`, so whether a write's result survives depends on which writer method the command happens to use.
  A script can't rely on `-q` for "print nothing on success" or for "still give me the id".
- **Workaround:** don't use `-q` for writes; discard stdout instead.
- **Suggestion:** pick one (conventions 6.2's "drop write results" is the simpler rule for scripts) and make `--help` and the writer say the same.

### L-105 · Bug 🟠 · -o human prints only "✓ Done" for every command that returns an object

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T3, T9, T10 (found independently by three lanes)
- **Repro:**
  ```
  $U content get $(id .home) -o human            -> ✓ Done   (exit 0)
  $U webhook get r5b-hook -o human               -> ✓ Done
  $U health run Security -o human                -> ✓ Done
  $U server info -o human / auth whoami / document-type get blogYear / user-group get blogEditors -> ✓ Done
  $T schema export -o human                      -> ✓ Done   (no --out: the snapshot is lost)
  $T data-type folder create --name X -o human   -> ✓ Done   (no id; data-type folders can't be listed)
  $U media list -o human                         -> a table (lists are fine)
  ```
  `HumanOutputWriter.WriteSuccess` is `AnsiConsole.MarkupLine("[green]✓[/] Done")` and ignores `data` (its own comment says objects should be pretty-printed). All 61 `RunObjectAsync` call sites go through it.
- **Expected:** human is the default output in a terminal (`--output` help), so `umbraco content get <id>` typed by a person shows nothing, and a create doesn't show the new id.
- **Workaround:** `-o json`.
- **Suggestion:** render `data` as a key/value grid (nested values as compact JSON) and keep "Done" for empty results; a unit test that `get -o human` output contains the id.

### L-106 · Bug 🟠 · -v redaction misses webhook header values and other credential-shaped keys

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T3, #166 re-test (two lanes)
- **Repro:**
  ```
  $U webhook create --name r5d-hook --url http://127.0.0.1:9/r5d --event Umbraco.MediaSave \
     --header 'Authorization=Bearer r5d-secret' --header 'X-Api-Key=r5d-apikey' \
     --header 'X-Auth-Token=r5d-tok' --header 'Cookie=sess=r5d-cookie' -v 2>err
  > {..."headers":{"Authorization":"Bearer r5d-secret","X-Api-Key":"r5d-apikey","X-Auth-Token":"[redacted]","Cookie":"sess=r5d-cookie"}...}
  < {..."headers":{"Authorization":"Bearer r5d-secret","Cookie":"sess=r5d-cookie","X-Api-Key":"r5d-apikey","X-Auth-Token":"[redacted]"}}
  ```
  The same values leak on `webhook get/list/update -v`. `webhook get <name>` lists every webhook to resolve the name, so `-v` logs all webhooks' headers. `webhook log list -v` prints Umbraco's `requestHeaders` string (`Authorization: Bearer abc123\r\nX-Api-Key: ...`) in clear.
  `VerboseHttpHandler.IsSecretName` is a substring match on `password|secret|token|apikey|api_key`. A crafted `data-type create --json-body -v` showed what it misses: `api-key`, `pwd`, `passphrase`, `Authorization` (as a JSON key), `auth`, `credentials`, `privateKey`, `connectionString`, `cookie`, `sessionId`, URLs with `user:pass@` or `?token=`, and `{"alias":"apiKey","value":"..."}` name/value pairs. What it catches: `apiKey`, `API_KEY`, `clientSecret`, `PassWord`, `accessToken`, `refresh_token`, nested in arrays too; member `password`/`newPassword` are safe.
- **Expected:** `--verbose` help says "Secrets are redacted". Webhook headers exist to carry credentials (the CLI's own example is `--header X-Api-Key=abc123`), and the `Authorization` request header is already redacted.
- **Workaround:** don't use `-v` on webhook commands, or don't share the log.
- **Suggestion:** redact every value under a webhook `headers` object and `Authorization:` lines in `requestHeaders`; normalise names by dropping `-`/`_` before matching; add `auth`, `authorization`, `cookie`, `credential`, `privatekey`, `passphrase`, `connectionstring`. Tests for `X-Api-Key` and an `Authorization` JSON property.

### L-107 · Bug 🟠 · schema diff compares containers and properties by array position, so a hand-written snapshot never re-diffs clean

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Partial snapshots (#338), on `sites/scratch`
- **Repro:** containers written in the natural order `Content`, `Content/Hero`, `Settings`, `Settings/Hero`, each with an explicit `sortOrder`. Apply succeeds, then:
  ```
  $X schema diff p1.json | jq -c '[.data[]|select(.change!="Removed")|.changes]'
  [["containers[2].name",...,"containers[3].id","cleanup"], ...]
  ```
  Umbraco returns them as `Content, Hero(Content), Hero(Settings), Settings`; with the file in that order (and `cleanup`), the diff is `[]`. Properties behave the same way: the agent guide's own flow (`document-type get r5eArticle -o json | jq '{schemaVersion:"4",documentTypes:[.data]}'`, append a property with `"dataType":"Textstring"` and `"container":"Content/Hero"`) applies correctly but re-diffs as `Changed ["properties"]`, because live puts the new property before the `Settings/Hero` one. Every re-apply plans an update.
- **Expected:** `schema diff` is `[]` after a successful apply. Order is carried by `sortOrder` and `parent`, not by array position.
- **Workaround:** write containers and properties in Umbraco's order, which you only learn by exporting.
- **Suggestion:** match containers by (type, name, parent path) and properties by alias, whatever their position; report a real `sortOrder`/`parent` change instead.

### L-108 · Bug 🟠 · schema diff: fields a hand-written snapshot omits show as Changed

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Partial snapshots (#338), on `sites/scratch`
- **Repro:** the `add-subtitle.json` example from `docs/commands.md`, exactly as written:
  ```
  $X schema apply add-subtitle.json   -> create documentType blogPost: success
  $X schema diff add-subtitle.json | jq -c '[.data[]|select(.change!="Removed")|.changes]'
  [["cleanup"]]
  ```
  A data-type-only file (`{"name":"r5e Headline","editorAlias":...,"values":[...]}`) re-diffs as `["isDeletable","canIgnoreStartNodes"]`: server-computed, read-only flags (live `true`/`true`). Absent vs `null` is already equal (81d3408); absent vs a non-null server default is not.
- **Expected:** a field left out of a hand-written entry isn't compared (or is compared with the server default), and read-only fields are never compared. The docs' own example should re-diff clean.
- **Workaround:** add `"cleanup":{"preventCleanup":false,"keepAllVersionsNewerThanDays":null,"keepLatestVersionPerDayForDays":null}` and the live flag values by hand.
- **Suggestion:** drop `isDeletable`/`canIgnoreStartNodes` in the comparison normaliser; treat an absent object field as not managed.

### L-109 · Bug 🟡 · --dry-run prints passwords and secret headers in clear, while -v redacts the same body

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Users (#334), #166 re-test (two lanes)
- **Repro:**
  ```
  $X user update r5e.sec@example.com --new-password 'NewSecret98765!' --dry-run            -> "body": { "newPassword": "NewSecret98765!" }
  $U member create --email r5d-dry@example.com --name r5d-dry --member-type siteMember --password 'R5d-Secret123!' --dry-run  -> "password": "R5d-Secret123!"
  $U member update <id> --new-password 'R5d-NewSecret456!' --dry-run                       -> "newPassword": "R5d-NewSecret456!"
  $T webhook create ... --header 'Authorization=Bearer r5d-secret' --dry-run                -> "headers":{"Authorization":"Bearer r5d-secret"}
  $X user update ... --new-password short1 -v 2>&1 | grep newPassword                       -> > {"newPassword":"[redacted]"}
  ```
- **Expected:** the same redaction as `-v`. `--dry-run` is the recommended CI preview step, so it ends up in CI logs and agent transcripts.
- **Workaround:** don't dry-run password or header changes.
- **Suggestion:** run the dry-run body through the verbose redactor (after L-106), perhaps with a `--show-secrets` escape hatch.

### L-110 · Improvement 🟡 · --dry-run previews only the first request of a multi-step user write

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Users (#334)
- **Repro:** `$X user create ... --password 'DryRunPw98765!' --dry-run` shows only `POST /user` (no change-password step, no rollback). `$X user update r5e.sec@example.com --name Z --new-password '...' --disabled --dry-run` shows only the `PUT /user/{id}`.
- **Expected:** the preview lists every request the command would send (`--help`: "Preview the HTTP request a write command would send").
- **Suggestion:** return an array of the planned requests, secrets redacted (L-109).

### L-111 · Bug 🟡 · user-group create: response echoes the request instead of the saved group

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Users (#334)
- **Repro:**
  ```
  $X user-group create --alias r5eTmp --name "R5E tmp" -o json | jq -c '.data|{isDeletable,aliasCanBeChanged,description}'
  {"isDeletable":false,"aliasCanBeChanged":false,"description":null}
  $X user-group get r5eTmp -o json | jq -c '.data|{isDeletable,aliasCanBeChanged,description}'
  {"isDeletable":true,"aliasCanBeChanged":true,"description":""}
  ```
  `CreateUserGroupAsync` builds the response from the request; `update` returns the right values.
- **Expected:** create returns what was saved, as #310/#314 settled for the type creates. An agent reading `isDeletable:false` would think the group can't be deleted.
- **Workaround:** `user-group get` after the create.
- **Suggestion:** read the group back, as `user create` does.

### L-112 · Improvement 🟡 · user delete with several ids: the refusal doesn't say which user

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Users (#334), probe: deleting a signed-in user
- **Repro:**
  ```
  $X user delete r5e.two@example.com r5e.uno@example.com --yes
  Cannot delete user (CannotDeleteUserWithLoginHistory): This user has logged in and may be referenced by audit logs or content history. Disable the user instead of deleting them.
  ```
  Nothing is deleted (correct), but "This user" doesn't say which of the two.
- **Expected:** the message names the user(s) who have signed in and suggests `user update <id> --disabled`.
- **Workaround:** delete one at a time, or check `lastLoginDate` with `user get`.

### L-113 · Improvement 🟡 · user get: no content languages

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · re-test #216
- **Repro:** `$X user get r5e.uno@example.com -o json | jq '.data|keys'` has `sections` (the union of the groups' sections) and `languageIsoCode` (the back-office UI language), but nothing for the content languages the user may edit. The group has `languages:["en-US"]`.
- **Expected:** #216 asked for languages; shown like `sections`, e.g. `languages: [...]` or `hasAccessToAllLanguages: true`.
- **Workaround:** `user-group get` on each group.

### L-114 · Bug 🟡 · schema apply: a bare name in allowedDocumentTypes passes dry-run, then fails mid-apply

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Partial snapshots (#338)
- **Repro:** `min-adt.json` = a new data type `r5e Min` plus doc type `r5eMin` with `"allowedDocumentTypes": ["r5ePermPage"]`:
  ```
  $X schema apply min-adt.json --dry-run  -> 2 creates "planned", success
  $X schema apply min-adt.json
  Apply failed on create documentType 'r5eMin' (1 change(s) applied before the failure): ... $.allowedDocumentTypes[0]: The JSON value could not be converted to ...DocumentTypeSort.
  $X data-type get "r5e Min"  -> success (left behind)
  ```
  `docs/commands.md` lists `allowedDocumentTypes` among references that "may name their target ... as a bare string"; `allowedTemplates: ["r5eArticle"]` does work that way. The working form is `[{"documentType":"r5ePermPage","sortOrder":0}]`.
- **Expected:** accept the bare string, or reject it with `invalid_argument` before any write, as the name checks already do.
- **Workaround:** use the `{documentType, sortOrder}` shape.
- **Suggestion:** accept both shapes; check item shapes in the pre-write reference pass; say in the docs which field inside each item holds the reference.

### L-115 · Bug 🟡 · Document types resolve by alias only, not name

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Partial snapshots (#338)
- **Repro:**
  ```
  $X document-type get "R5E Perm Page"                -> No document type found with alias 'R5E Perm Page'.
  snapshot compositions: [{"documentType":"R5E Perm Page","compositionType":"Composition"}]
                                                      -> ... the instance answered: No document type found with alias 'R5E Perm Page'.
  $X media-type get "Vector Graphics (SVG)"           -> success (umbracoMediaVectorGraphics)
  ```
- **Expected:** `docs/conventions.md` 3.2 says "alias, then name, ignoring case", and the schema docs say names are looked up "by alias then name ... the same rules as every `<id>` argument". Case-insensitive alias works (`r5epermpage`).
- **Workaround:** use the alias.

### L-116 · Improvement 🟡 · A snapshot entry's name silently shadows a live type's alias

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Partial snapshots (#338)
- **Repro:** the snapshot creates `r5eChild` with name `"r5ePermPage"`, and `r5eParent2` with `allowedDocumentTypes:[{"documentType":"r5ePermPage",...}]`. After apply, `r5eParent2` allows `28ca2194...` (r5eChild), not the live type whose alias is `r5ePermPage` (`91d21557...`). No warning.
- **Expected:** a reference that matches one thing in the snapshot and another on the instance is ambiguous; the docs say a name that "matches ... more than one thing fails".
- **Suggestion:** fail listing both candidates, or prefer an exact alias match wherever it comes from.

### L-117 · Bug 🟡 · commands.md says prune refuses every document/media type delete

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · Partial snapshots (#338)
- **Repro:** `docs/commands.md` (~line 1129): "any document or media type (Umbraco cannot say how many items use one) ... are refused unless `--force`". Live: a `documentTypes`-only file without `blogPost` (no content) pruned it with `--prune --yes`, no `--force`; leaving out `r5ePermPage` (2 docs) is refused with "Document type 91d21557-... has 2 document item(s)...". Since #287 the guard counts items.
- **Expected:** the docs match the behaviour.
- **Suggestion:** update the sentence; name the type by alias in the refusal, not only by id.

### L-118 · Bug 🟡 · document-blueprint list shows `documentType.alias: ""`; blueprint get/scaffold and content find rows have no alias

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T6, T2
- **Repro:**
  ```
  $U document-blueprint list --parent <folder> -o json | jq -c '.data[0].documentType'  -> {"id":"e7eeefdc-...","alias":""}
  $U document-blueprint get <bp> -o json | jq -c .data.documentType                     -> {"id":"e7eeefdc-...","icon":"icon-rss","collection":null}   (scaffold the same)
  $U content find --name docker -o json | jq -c '.data[0].documentType'                 -> {"id":"e7eeefdc-...","icon":"icon-rss"}
  $U content list --parent <year> -o json | jq -c '.data[0].documentType'               -> {"id":"e7eeefdc-...","alias":"blogPost","icon":"icon-rss"}
  ```
- **Expected:** `alias: "blogPost"` everywhere, as in `content get/list` (#284) and `version get` (#320). The empty string is the bug #340 fixed for `document-type list`.
- **Workaround:** `document-type get <id>`.
- **Suggestion:** fill the alias from the id-to-alias cache in blueprint list/get/scaffold and content find.

### L-119 · Bug 🟡 · publish/unpublish `cultures` echoes the request

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · re-test #325/#345
- **Repro:** invariant `contactSubmission` document:
  ```
  $U content publish $I -o json | jq -c .data.cultures                  -> null
  $U content publish $I --culture en-US -o json | jq -c .data.cultures  -> ["en-US"]   (content get: [{"culture":null,"state":"Published"}])
  $U content unpublish $I --culture en-US --yes -o json | jq -c .data   -> {"id":"...","cultures":["en-US"]}
  ```
  Variant post: `unpublish --culture da-DK` -> `["da-DK"]`, then `unpublish --yes` (no culture) -> `["en-US","da-DK"]` although da-DK was already Draft.
- **Expected:** #344/#345: `cultures` lists what was published or unpublished, `null` for an invariant document. The same effect should give the same report.
- **Workaround:** re-read `content get`.
- **Suggestion:** resolve `--culture` against the document read already made: `null` for invariant, and for unpublish only the cultures that were live. Or reject `--culture` on an invariant document.

### L-120 · Improvement 🟡 · content move/copy/sort: Umbraco's generic 400 passes through with no hint

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T2, #342 (`content move <id>   # to the content root` example)
- **Repro:**
  ```
  $U content move <blogPost>                -> 400 "Operation not permitted (NotAllowed): The attempted operation was not permitted, likely due to a permission/configuration mismatch with the operation."
  $U content move <blogPost> --parent <home> -> same
  $U content copy <blogPost>                -> 400 "The attempted operation was not permitted, ..." (no title prefix)
  $U content sort --parent <year> --order <id not a child> -> 400 "Invalid sorting options (SortingInvalid): ... Additional details can be found in the log."
  ```
- **Expected:** a message naming the cause, like `content restore` got in #230/#266: e.g. "blogPost is not allowed under <parent> (or at the root); check the parent type's allowed children", and for sort "these ids aren't children of <parent>: ...".
- **Workaround:** check the parent type's allowed children with `document-type get`, and `content list --parent`.
- **Suggestion:** reword NotAllowed for move/copy as restore does; compare `--order` with the parent's children before sending.

### L-121 · Gap 🟡 · No way to list the recycle bin, so `empty-recycle-bin --yes` deletes items you can't see

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T2
- **Repro:** `umbraco commands` has only `trash`, `restore` and `empty-recycle-bin` for content and media. The confirmation says "Permanently delete ALL items in the content recycle bin?" and `--dry-run` shows only `DELETE .../recycle-bin/document`. On a shared site the only way to see what would go was `GET /umbraco/management/api/v1/recycle-bin/document/root`.
- **Expected:** a list before a permanent bulk delete, and a way to find trashed ids to `restore`.
- **Workaround:** Management API `recycle-bin/document/root` and `/children` (`recycle-bin/media/...`).
- **Suggestion:** `content recycle-bin list` / `media recycle-bin list` (or `list --trashed`); have `empty-recycle-bin --dry-run` list the items.

### L-122 · Gap 🟡 · stylesheet/script/partial-view: no rename for files or folders

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T4
- **Repro:** `stylesheet|script|partial-view --help` list only list/get/create/update/delete/folder. The Management API has `PUT /stylesheet/{path}/rename`, `/script/{path}/rename` and `/partial-view/{path}/rename`.
- **Expected:** a rename, like the back office's Rename action. Today it's get, create, delete: three calls, not atomic.
- **Workaround:** `get -o json | jq -j .data.content > f`, `create --name new --content-file f`, `delete old --yes`.
- **Suggestion:** `stylesheet|script|partial-view rename <path> --name <new>`.

### L-123 · Bug 🟡 · webhook log list help example uses `.data.items[]`

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T3
- **Repro:**
  ```
  $U webhook log list r5b-hook -o json | jq '.data.items[] | select(.isSuccessStatusCode | not)'
  jq: error (at <stdin>:1395): Cannot index array with string ("items")
  ```
  `data` is an array, with paging in `meta`.
- **Expected:** a help example that runs.
- **Workaround:** `.data[]`.
- **Suggestion:** fix the example; consider a test that checks help-example jq paths against the envelope shape.

### L-124 · Gap 🟡 · You can't remove one webhook header

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T3
- **Repro:**
  ```
  $U webhook update r5b-hook --header "X-Test=" -o json | jq -c .data.headers
  {"Authorization":"Bearer abc123","X-Api-Key":"sekret999","X-Auth-Token":"tok777","X-Test":""}   (the next delivery carried an empty X-Test)
  $U webhook update r5b-hook --header X-Test
  Each --header must be name=value, e.g. X-Api-Key=abc123. Not understood: X-Test.
  ```
- **Expected:** a way to drop one header without retyping the others.
- **Workaround:** `--replace --yes` with every header to keep, which puts secrets back on the command line and clears the type filter unless `--type` is repeated.
- **Suggestion:** `--remove-header <name>` (repeatable), or treat `Name=` as removal and document it.

### L-125 · Improvement 🟡 · webhook --type: no suggestion, and no check against the events

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T3
- **Repro:**
  ```
  $U webhook update r5b-hook --type blogpst
  No document type, media type or member type has the alias 'blogpst'. Use 'umbraco document-type list', ...
  $U webhook create --name r5b-x --url http://127.0.0.1:8791/x --event Umbraco.MediaSave --type blogPost --dry-run -o json | jq -c .data.body.contentTypeKeys
  ["e7eeefdc-f2fd-433e-93c1-f0944eb5bae3"]     (a document type on a media-only webhook: accepted)
  ```
- **Expected:** "did you mean 'blogPost'?" as `--event` gives. A type that can never match the events should be refused or warned about, as unknown events are ("Umbraco would save the webhook but never fire it").
- **Workaround:** check aliases with `document-type list` / `media-type list`.
- **Suggestion:** reuse the #278 did-you-mean helper; warn when no type's kind matches any event's `eventType`.

### L-126 · Bug 🟡 · --all repeats rows at page boundaries while the list grows

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #196 re-test
- **Repro:** on source, with a loop of failed token POSTs keeping the log growing:
  ```
  $U log-viewer list --all -o json | jq '[.meta.total, (.data|length), ([.data[]|tojson]|unique|length)]'
  [4147,4147,3960]                  (187 repeated rows, in pairs at every multiple of 100)
  $U log-viewer list --all --asc -o json | jq ...
  [3731,3731,3728]                  (only Umbraco's own 3 identical events)
  ```
  `CollectAllPagesAsync` pages with `skip = collected.Count`, 100 at a time. Newest-first, each new entry shifts the offsets, so each page starts with the tail of the page before. `meta.total` is the collected count, so it reports 4147 with `hasMore:false`.
- **Expected:** `--all` returns each entry once, and `total` is the real count.
- **Workaround:** `log-viewer list --all --asc`, or pin `--end-date` to now.
- **Suggestion:** under `--all`, fix `endDate` to the time of the first request (or page ascending and reverse).

### L-127 · Improvement 🟡 · health run results carry only each check's id

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T9
- **Repro:**
  ```
  $U health run Security -o json | jq -c '.data.checks[0]'
  {"id":"ed0d7e40-...","results":[{"message":"The application URL is not available...","resultType":"Info",...}]}
  ```
  Which check it is ("Click-Jacking Protection") needs `health get Security` and a join on id; the order differs from `get`.
- **Expected:** a result readable on its own, as the back office shows the check name beside each result.
- **Workaround:** join with `health get <group>`.
- **Suggestion:** add `name` (and `description`) to each check from the group read.

### L-128 · Improvement 🟡 · property-type is-used returns false for an alias the type doesn't have

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T8
- **Repro:**
  ```
  $U property-type is-used --document-type blogPost --alias nope -o json   -> {"status":"success","data":false}
  $U property-type is-used --document-type nopeType --alias author        -> invalid_argument "No document type found with alias 'nopeType'..."
  ```
  Umbraco itself returns `false`/200; the CLI already reads the type, so it could check the alias.
- **Expected:** like an unknown type: a typo shouldn't read as "not in use, safe to remove".
- **Workaround:** check the alias with `document-type get` first.
- **Suggestion:** fail with `invalid_argument` when the alias isn't on the type or its compositions, listing the aliases it has.

### L-129 · Improvement 🟡 · user-data get on a missing key says "unexpected HTTP 404 with no error details"

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T9
- **Repro:**
  ```
  $T user-data get 11111111-2222-3333-4444-555555555555 -> 404 "The Umbraco server returned an unexpected HTTP 404 with no error details."
  $T webhook get <same>   -> 404 "The webhook could not be found."
  $T data-type get <same> -> 404 "The data type could not be found (NotFound)."
  ```
- **Expected:** a not-found message like other `get` commands; an empty 404 on a by-key read means "not found".
- **Suggestion:** map an empty 404 on `user-data get/update/delete` to "No user data with key <key>."

### L-130 · Improvement 🟡 · -v redacts failedPasswordAttempts / lastPasswordChangeDate

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #166 re-test
- **Repro:**
  ```
  $U member get <id> -v 2>&1 >/dev/null | grep -o '"[A-Za-z]*":"\[redacted\]"'
  "failedPasswordAttempts":"[redacted]"  "lastPasswordChangeDate":"[redacted]"
  $U user list -v   -> "lastPasswordChangeDate":"[redacted]"
  ```
  Numbers, booleans and whole objects under a matching key are replaced too (`"secretNum":1234`, `"tokenObj":{...}`). `member get` stdout doesn't include these fields, so `-v` is the only place to see them.
- **Expected:** the substring rule shouldn't hide exactly the fields needed when debugging a lockout.
- **Workaround:** the Management API.
- **Suggestion:** redact only string values; keep numbers, booleans and dates.

### L-131 · Gap 🟡 · -v logs nothing for auth login, auth doctor or the token exchange

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #166 re-test
- **Repro:**
  ```
  umbraco --config <tmp> auth login -p V --host https://localhost:44802 --client-id .. --client-secret .. -v 2>err  -> err is empty
  UMBRACO_NO_TOKEN_CACHE=1 umbraco --config <tmp> auth doctor -p V -v 2>err                                        -> err is empty (doctor makes 5+ calls)
  env credentials, server info -v                                                                                  -> only GET /server/information; no POST /token
  ```
  A bad secret with `-v` shows only the final error.
- **Expected:** `-v` help says "each HTTP request", and auth failures are the most common reason to reach for `-v`.
- **Workaround:** `auth doctor` without `-v`, or curl.
- **Suggestion:** log the token POST (the handler already redacts `client_secret` form fields) and route doctor/login through the verbose client.

### L-132 · Bug 🟡 · completion scripts carry the build checkout's line endings

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #92 re-test
- **Repro:** the alpha.14 dll (packed on Windows) in `mcr.microsoft.com/dotnet/sdk:10.0` with zsh 5.9:
  ```
  umbraco completion zsh | od -c | head -2    -> ... # c o m p d e f   u m b r a c o \r \n
  zsh -n _umbraco                              -> _umbraco:16: parse error near `\n'
  bash -c 'eval "$(umbraco completion bash)"' -> syntax error near unexpected token `$'{\r''
  umbraco completion zsh | tr -d '\r' > f; zsh -n f   -> OK; con<TAB> -> content, content cr<TAB> -> create, --doc<TAB> -> --document-type
  ```
  The scripts are raw string literals in `CompletionCommand.cs`, so they take the checkout's line endings (`core.autocrlf=true`). `publish.yml` packs on ubuntu, so a CI-built release is probably LF; any Windows-built package is broken for zsh and Linux bash.
- **Expected:** LF scripts whatever the build OS.
- **Workaround:** `| tr -d '\r'`.
- **Suggestion:** `.ReplaceLineEndings("\n")` on the constants, and a unit test asserting no `\r`.

### L-133 · Improvement 🟡 · content create --example body isn't round-trippable

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #174 re-test
- **Repro:**
  ```
  $T content create --example --document-type blogPost -o json > ex.json
  jq --arg p <2026 folder id> '.data|.parent={id:$p}|.variants[0].name="r5d-example-post"' ex.json | $T content create --json-body -
  -> error 500: "The JSON value could not be converted to System.Guid. Path: $[0].mediaKey ..." (plus a server stack trace in details)
  ```
  The example holds `"mediaKey":"<media id>"` (featuredImage) and `"value":"<document id>"` (relatedPost). With real ids substituted, all 13 values are created correctly.
- **Expected:** the example works as printed, or the CLI says which fields to fill.
- **Workaround:** replace the placeholders by hand.
- **Suggestion:** fill picker values with a real media/document (the command has a host), or omit them; reject `"<... id>"` strings client-side naming the alias.

### L-134 · Bug 🟡 · help text and catalog requiredUnless disagree

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #84 re-test
- **Repro:**
  ```
  $U content update --help   -> --json-body "... Required unless --schema is used."
  $U commands | jq '... content update --json-body .requiredUnless'  -> ["--schema","--template"]
  $T content update <home> --dry-run  -> "Supply the content id and --json-body (or --template on its own)."
  document-blueprint create --document-type: help "Required unless --json-body or --schema"; requiredUnless ["--json-body","--schema","--from-document"]
  ```
- **Expected:** one source of truth; the catalog matches the behaviour, the help text is stale.
- **Workaround:** trust `umbraco commands`.
- **Suggestion:** generate the "Required unless ..." sentence from the `RequiredUnless` data.

### L-135 · Bug 🟡 · member create result has createDate 0001-01-01

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #166 re-test (side check)
- **Repro:**
  ```
  $U member create --email r5d-member@example.com --name r5d-member --member-type siteMember --password '...' -o json | jq .data.createDate
  "0001-01-01T00:00:00+00:00"
  $U member get <id> | jq .data.createDate   -> "2026-09-28T19:41:13.1253363+00:00"
  ```
- **Expected:** the real date, or no field (compare #42, #202).
- **Workaround:** `member get`.
- **Suggestion:** read the member back after create, as `webhook create` does.

### L-136 · Improvement 🟡 · completion rough edges

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · #92 re-test
- **Repro:**
  - bash: `umbraco -o <TAB>` offers nothing (no `AcceptOnlyFromAmong(json,human,csv)`), so all shells fall back to file names.
  - `umbraco content <TAB>` offers `/? /h -?` alongside `--help`.
  - pwsh: `TabExpansion2 'umbraco con' 11` gives `content content-types --config`: the completer returns the CLI's matches unfiltered, so Tab can swap `con` for `--config`.
  - All three scripts run bare `umbraco`, so whichever is first on PATH answers (here a global alpha.11, which offered the removed `content-types` noun). Wrappers such as `sites/source/umb` get no completion.
- **Expected:** value completion for enum options, and the prefix filter pwsh needs.
- **Suggestion:** `AcceptOnlyFromAmong` on `--output`; hide `/?` `/h`; pwsh: `Where-Object { $_ -like "$wordToComplete*" }`; optionally `completion <shell> --command-name <name>`.

### L-137 · Improvement 🟡 · config file persists computed IsComplete / EffectiveDefault

- **Found:** 2026-09-28 · CLI 0.1.0-alpha.14 · T10
- **Repro:** `auth login -p A` into an empty `--config` writes `"profiles":{"A":{...,"IsComplete":true}}, "EffectiveDefault":"A"`. After `auth logout` of A, the file still has `"defaultProfile":"A","EffectiveDefault":"A"` and no profile A. Behaviour is correct ("No default profile is set (it was logged out of)").
- **Expected:** only stored fields, camelCase like the rest of the file.
- **Suggestion:** `[JsonIgnore]` on `CliConfig.IsComplete` and `ConfigFile.EffectiveDefault`.
