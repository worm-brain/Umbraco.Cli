# Alpha test guide (agent runbook)

A deterministic, re-runnable acceptance test for the `umbraco` CLI, written for an **AI agent**
to execute end to end against a live Umbraco instance. It exercises functionality **and**
verifies that the documentation matches reality.

- **Target of this run:** the alpha build under test (record the exact version in preflight).
- **Environment it was written for:** a dedicated Umbraco 14+ test instance. It is validated
  here against **v17**; any command whose behaviour differs on v17 is a finding, not a guide bug.
- **Safety:** safe by default. Nothing outside a namespaced test area is ever mutated, and the
  five global side-effect commands are skipped unless a human explicitly opts in (see [SS2](#ss2-safety-tiers)).

---

## Operating contract (read this first, then act)

> You are the **test agent**. Do exactly this:
>
> 1. Do [SS1 Preflight](#ss1-preflight). If any preflight check fails, **stop** and report - do
>    not test against an unconfigured or unknown instance.
> 2. Read [SS2 Safety tiers](#ss2-safety-tiers) and [SS3 Conventions](#ss3-conventions). You must
>    honour them for every command.
> 3. Execute phases [A](#phase-a--self-description--doc-accuracy) through [G](#phase-g--bulk-operations) **in order** (or fan them out per
>    [SS7 Orchestration](#ss7-orchestration-subagents-models-skills)). For every test, run the command, compare against
>    **Expect**, and append one row to `ledger.jsonl` ([SS4](#ss4-results-ledger)).
> 4. **Do not run [Phase H](#phase-h--danger--opt-in-default-skip) unless `RUN_DANGEROUS=1` and a human has confirmed the
>    instance is disposable.** Default is skip-and-record-as-SKIP.
> 5. Always run [SS6 Teardown](#ss6-teardown--baseline-verification), even after failures, so the instance returns to baseline
>    and the run is repeatable.
> 6. Produce `findings.jsonl` ([SS5](#ss5-raising-issues)) and a `summary.md`. Stop. Do not file GitHub
>    issues yourself unless told to - the human/Claude picks up `findings.jsonl` and triages.
>
> **Golden rule:** never create, update, delete, publish, unpublish, move, or trash any entity
> you did not create in this run. Track every id you create; mutate only those.

---

## Contents

- [SS1 Preflight](#ss1-preflight)
- [SS2 Safety tiers](#ss2-safety-tiers)
- [SS3 Conventions (namespace, ids, assertions, normalization)](#ss3-conventions)
- [SS4 Results ledger (the re-runnable output)](#ss4-results-ledger)
- [SS5 Raising issues (the handoff to Claude)](#ss5-raising-issues)
- [SS6 Teardown & baseline verification](#ss6-teardown--baseline-verification)
- [SS7 Orchestration: subagents, models, skills](#ss7-orchestration-subagents-models-skills)
- [Phase A - Self-description & doc accuracy](#phase-a--self-description--doc-accuracy)
- [Phase B - Output contract & global flags](#phase-b--output-contract--global-flags)
- [Phase C - Guardrails](#phase-c--guardrails)
- [Phase D - Read-only coverage sweep](#phase-d--read-only-coverage-sweep)
- [Phase E - Scoped write lifecycles](#phase-e--scoped-write-lifecycles)
- [Phase F - Schema & content pipelines](#phase-f--schema--content-pipelines)
- [Phase G - Bulk operations](#phase-g--bulk-operations)
- [Phase H - DANGER / opt-in](#phase-h--danger--opt-in-default-skip)
- [Appendix: fixed test ids and jq helpers](#appendix-fixed-test-ids-and-jq-helpers)

---

## SS1 Preflight

Create a run directory and record the environment. This stamps the run so results are
comparable across re-runs.

```bash
export RUN_DIR="./test-runs/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$RUN_DIR"
: > "$RUN_DIR/ledger.jsonl"          # the canonical, re-runnable output
: > "$RUN_DIR/findings.jsonl"        # issues to hand off

# Auth: prefer environment variables (nothing written to disk; agent-safe).
export UMBRACO_HOST="https://<the-test-site>"
export UMBRACO_CLIENT_ID="<api-user-client-id>"
export UMBRACO_CLIENT_SECRET="<api-user-client-secret>"
```

Preflight checks (all must pass before you continue):

| ID | Command | Expect |
|---|---|---|
| P-1 | `umbraco --version` | exit 0; record the string as `cliVersion`. Must be the alpha build under test. |
| P-2 | `umbraco auth doctor --output json` | exit 0; every check `pass` (or `warn`). Record the instance version as `serverVersion`. |
| P-3 | `umbraco server info --output json` | exit 0; record `.data` (version + runtime mode). Confirm the instance is **not** production, or that a human has approved it. |
| P-4 | `umbraco commands > "$RUN_DIR/catalog.json"` | exit 0; valid JSON. This is the test matrix source for Phase A and D. |

Write `$RUN_DIR/manifest.json`:

```json
{ "runId": "<RUN_DIR name>", "cliVersion": "...", "serverVersion": "...",
  "guideRev": "<git rev of this repo, or 'installed-tool'>", "runDangerous": false,
  "host": "<redacted or hostname only>", "namespace": "clitest" }
```

If P-1..P-4 do not all pass, **stop and report**: testing an unconfigured instance produces
meaningless results.

---

## SS2 Safety tiers

Every command falls into exactly one tier. The tier decides whether you may run it and how.

### Tier SAFE (run freely)
All reads: `list`, `tree`, `find`, `get`, `status`, `info`, `configuration`, `troubleshooting`,
`whoami`, `doctor`, `commands`, `--schema`, `--dry-run` on any write, `is-used`, `referenced-by`,
`versions`, `resize-urls`, `searcher query`, `log-viewer` reads, `manifest list`,
`relation`/`relation-type` reads, `tags`/`cultures` list. These never change server state.
(`content`/`media tree` and `find`, and `dictionary tree`, are reads.)

### Tier SCOPED-WRITE (allowed only on entities you created this run)
`create`, `update`, `delete`, `trash`, `restore`, `move`, `sort`, `copy`, `publish`, `unpublish`,
`publish-descendants`, `upload`, `add-users`/`remove-users`, `from-document`, `rollback`,
`bulk publish/unpublish/delete`, saved-search create/delete, `schema apply` / `content apply`.
(`content`/`media sort` reorders children; `dictionary move` reparents an item.)
Run these **only** against the `clitest` namespace and the fixed ids in the
[Appendix](#appendix-fixed-test-ids-and-jq-helpers). Every one is undone in [Teardown](#ss6-teardown--baseline-verification).

### Tier DANGER-OPTIN (default SKIP; requires `RUN_DANGEROUS=1` + human OK)
These have **instance-wide** or expensive/irreversible side effects and are never needed to
prove the CLI works:

- `content empty-recycle-bin`, `media empty-recycle-bin` - wipes the **whole** recycle bin,
  not just your test items. Default SKIP even with opt-in unless the bin is known-empty of real
  content.
- `models-builder build` - regenerates server-side source.
- `indexer rebuild` - expensive; degrades search while it runs.
- `redirect tracking enable` / `redirect tracking disable` - toggles a **site-wide** behaviour.
  If tested, read `redirect status` first and **restore the original value** after.
- `health run` - a POST that executes health checks (some checks may have side effects).

If a DANGER command is skipped, still write a ledger row with `status: "SKIP"` and
`note: "danger-optin"` so coverage accounting stays honest.

---

## SS3 Conventions

**Namespace.** Everything you create is prefixed `clitest` (aliases: `clitestDocType`,
`clitestDataType`; names: `clitest ...`). This makes test artifacts identifiable and cleanup
exhaustive.

**Fixed ids for determinism.** Pass `--id <fixed-guid>` on every create using the
[Appendix](#appendix-fixed-test-ids-and-jq-helpers) table. Umbraco 14+ honours client-supplied ids, so re-runs converge on the
same ids instead of piling up duplicates, and teardown can delete by known id. If a `create`
rejects or ignores `--id`, **record that as a finding** and fall back to capturing the
server-assigned id from the response for that entity's later steps.

**Assertion pattern.** For each test, assert on (a) the process exit code and (b) specific,
stable fields - never on a full-payload byte match. Pull stable fields with `--fields` or `jq`:

```bash
out=$(umbraco content list --take 1 --output json); rc=$?
echo "$out" | jq -e '.status=="success" and .meta.command=="content.list" and .meta.schemaVersion=="2"' >/dev/null
```

**Normalization (why re-runs match).** The envelope carries volatile fields (`meta.durationMs`,
`meta.timestamp`) and the server assigns timestamps/ids. Strip them before any output
comparison:

```bash
NORMALIZE='del(.meta.durationMs, .meta.timestamp)'
```

**What "same output" means.** Byte-identical output is impossible on a live system. The
**re-runnable artifact is `ledger.jsonl`**: the per-test `status` column (PASS/FAIL/SKIP/ERROR),
keyed by test `id`, is deterministic given the same CLI build and instance baseline. Comparing
two runs = diffing their ledgers by `id` (see [SS4](#ss4-results-ledger)). A fix flips a FAIL to PASS; nothing else
should move.

**Error handling.** Expected failures are **data**: the CLI writes an error envelope to stderr
and returns a non-zero exit. Assert on the exit code and the error `code`/`message`, not on an
exception. Exit codes: `0` success, `1` API/invalid-invocation, `2` aborted before running
(no host / not auth / allow-list / destructive-without-`--yes` / `--readonly`-blocked),
`130` cancelled.

---

## SS4 Results ledger

Append exactly one JSON object per test to `$RUN_DIR/ledger.jsonl`. Schema:

```json
{ "id": "T-B-03", "phase": "B", "title": "content.list emits the success envelope",
  "cmd": "umbraco content list --take 1 --output json",
  "expect": "exit 0; status=success; meta.schemaVersion=2",
  "status": "PASS", "exitCode": 0,
  "findingKey": null, "severity": null, "note": "" }
```

- `status`: `PASS` | `FAIL` | `SKIP` | `ERROR` (ERROR = the test harness itself broke, e.g.
  could not run the command).
- `findingKey`: on FAIL, a **stable** dotted key you reuse every run, e.g.
  `docaccuracy.commands-vs-docs.missing`, `contract.exitcode.readonly-block`,
  `content.update.no-op`. The key is how a re-run maps back to the same issue.
- Keep ids stable across guide versions. Never renumber a passing test.

At the end, write `$RUN_DIR/summary.md`: counts by status, the `cliVersion`/`serverVersion`,
and a list of every FAIL with its `findingKey`.

**Re-run comparison** (after a fix, run again into a new `$RUN_DIR`, then):

```bash
join -j1 -t$'\t' \
  <(jq -r '[.id,.status]|@tsv' run-A/ledger.jsonl | sort) \
  <(jq -r '[.id,.status]|@tsv' run-B/ledger.jsonl | sort) \
  | awk -F'\t' '$2!=$3{print $1"  "$2" -> "$3}'
```

The only lines that should appear are tests that were fixed (`FAIL -> PASS`) or newly broken
(`PASS -> FAIL`).

---

## SS5 Raising issues

The canonical output for triage is `$RUN_DIR/findings.jsonl` - one object per distinct FAIL.
**You (the test agent) produce this file; you do not open GitHub issues** unless explicitly
asked. Claude picks it up afterwards and files/dedupes issues.

Finding schema:

```json
{ "findingKey": "content.update.no-op",
  "type": "functionality | doc-accuracy | contract | guardrail | crash",
  "severity": "blocker | major | minor | docs",
  "title": "content update reports success but does not change the field",
  "command": "umbraco content update <id> --json-body -",
  "expected": "the named field is changed; a follow-up get reflects it",
  "actual": "exit 0, status=success, but get shows the old value",
  "repro": ["step 1 exact command", "step 2 exact command"],
  "serverVersion": "17.x", "cliVersion": "0.1.0-alpha.4",
  "suggestedLabels": ["bug", "content", "priority:P1"],
  "ledgerIds": ["T-E-12"] }
```

Deduplication (so re-runs and re-tests do not double-file): the `findingKey` is the identity.
When Claude files an issue, it embeds `<!-- finding-key: content.update.no-op -->` in the body,
so the next run can `gh issue list --search "finding-key: content.update.no-op"` and update
rather than duplicate.

Severity -> label map (labels that exist in this repo):

| Severity | Labels |
|---|---|
| blocker | `bug`, `priority:P0` |
| major | `bug`, `priority:P1` |
| minor | `bug`, `priority:P2` |
| docs | `documentation`, `priority:P2` |

Add one area label by subject: `content`, `media`, `auth`, `config`, `security`, `safety`
(guardrails), `agent-dx`/`dx` (contract/output), `api-client`, `coverage` (a missing command),
`error-handling`, `languages`, `webhooks`, `members`.

**The handoff (what Claude does next, after testing):**

1. Read `$RUN_DIR/findings.jsonl` and `summary.md`.
2. For each finding, `gh issue list --search "finding-key: <key>" --state all` to dedupe.
3. Draft each new issue with the **`write-story` skill** (imperative title, value-first sentence,
   rule-based acceptance criteria, explicit repro and verification step), then
   `gh issue create` with the mapped labels and the hidden `finding-key` marker. The repo's
   `.github/ISSUE_TEMPLATE` bug form is the shape to match.
4. On a **re-run after fixes**, for every `findingKey` that is now PASS, comment on the issue
   with the **`write-ticket-update` skill** ("Verified fixed in re-run `<runId>`, CLI `<ver>`")
   and close it. For any still-FAIL, comment that it reproduces on `<runId>`.

---

## SS6 Teardown & baseline verification

Run this even if earlier phases failed. Delete in reverse dependency order, by fixed id, with
`--yes` (non-interactive). Deletes here only touch `clitest` artifacts you created.

```bash
# Content first (children before parents is handled by deleting the test root subtree),
# then schema, then any other created entities. Each guarded by --yes.
umbraco content delete "$CLITEST_CONTENT_CHILD" --yes || true
umbraco content delete "$CLITEST_CONTENT_ROOT"  --yes || true
umbraco document-blueprint delete "$CLITEST_BLUEPRINT" --yes || true
umbraco document-type delete "$CLITEST_DOCTYPE" --yes || true
umbraco media delete "$CLITEST_MEDIA" --yes || true
umbraco media-type delete "$CLITEST_MEDIATYPE" --yes || true
umbraco data-type delete "$CLITEST_DATATYPE" --yes || true
umbraco user-group delete "$CLITEST_USERGROUP" --yes || true
umbraco language delete "$CLITEST_LANG" --yes || true
umbraco dictionary delete "$CLITEST_DICT" --yes || true
umbraco webhook delete "$CLITEST_WEBHOOK" --yes || true
umbraco member-group delete "$CLITEST_MEMBERGROUP" --yes || true
umbraco member delete "$CLITEST_MEMBER" --yes || true
umbraco script delete "clitest/clitest.js" --yes || true
umbraco stylesheet delete "clitest/clitest.css" --yes || true
umbraco partial-view delete "clitest/clitest.cshtml" --yes || true
umbraco log-viewer saved-search delete "clitest-errors" --yes || true
```

Baseline check: re-list each noun with `--fields id` and assert no `clitest*` id remains. Record
a `T-Z-*` ledger row per noun. **A left-behind artifact is itself a finding** (`teardown.<noun>.residue`).

> Do **not** use `empty-recycle-bin` in teardown. Delete your own items by id; leave the shared
> bin alone.

---

## SS7 Orchestration: subagents, models, skills

Run solo, or fan out for speed. Phases are mostly independent; the write phases share tree state
so keep those on one worker or scope them to disjoint subtrees.

**Subagents (via the `Agent` tool).**

- Give each subagent: the auth env vars, `$RUN_DIR`, the fixed-id table, and the assertion
  contract from [SS3](#ss3-conventions). Have each **return** its ledger fragment (JSONL text); the orchestrator
  concatenates and writes the file. Return-and-merge avoids write contention and keeps output
  deterministic.
- Use `subagent_type: "general-purpose"` (it has Bash + file tools). `Explore` is read-only -
  fine for Phase A doc-accuracy diffing, not for anything that runs write commands.

**Model choice (the `model` override on `Agent`).**

| Work | Suggested model | Why |
|---|---|---|
| Mechanical execution (Phases B, D, E, G) | `haiku` or `sonnet` | Run command, assert JSON, emit a row. Cheap and fast; low judgement. |
| Doc-accuracy analysis (Phase A) & pipelines (Phase F) | `sonnet` | Comparing catalog vs docs and reasoning about diff/apply plans needs more care. |
| Triage, dedupe, issue drafting (handoff) | `opus` or `sonnet` | Judgement about severity, duplicates, and clear write-ups. |

Parallelisation plan that is safe: run Phases A, B, C, D as four concurrent `haiku`/`sonnet`
general-purpose subagents (all read-only or self-contained), collect fragments, then run
Phases E -> F -> G **sequentially on one worker** (shared content tree), then Teardown.

**Skills to use.**

- `write-story` - draft each finding as a well-formed GitHub issue (handoff step 3).
- `write-ticket-update` - the resolution/verification comment on re-run (handoff step 4).
- `understand` - optional, to deep-dive a confusing failure before writing it up.

Do not use `fork` for the parallel phases (it clones full context and bloats each worker); use
fresh `general-purpose` subagents with a tight brief. Reserve `fork` for when a worker genuinely
needs this run's accumulated context.

---

## Phase A - Self-description & doc accuracy

Verifies the CLI describes itself correctly and that `docs/commands.md` matches the real surface.
This is the doc-accuracy heart of the run. (If the CLI is an installed tool without the repo
checked out, `git clone` it or fetch `docs/commands.md` from the pinned release tag; record which.)

| ID | Check | Expect / finding |
|---|---|---|
| T-A-01 | `umbraco commands` is valid JSON with `.data.commands[]` | exit 0; parses. Else `docaccuracy.catalog.invalid`. |
| T-A-02 | Every leaf command in `catalog.json` appears in `docs/commands.md` as `umbraco <name>` | For each catalog leaf, grep the doc. Missing -> `docaccuracy.commands-vs-docs.missing` (list the names). |
| T-A-03 | Every ``umbraco `` invocation in `docs/commands.md` exists in the catalog | Reverse check. A documented-but-absent command -> `docaccuracy.docs-vs-commands.phantom`. |
| T-A-04 | Each catalog command's `destructive` flag matches its doc note | Docs say "needs --yes" iff catalog `destructive:true`. Mismatch -> `docaccuracy.destructive-flag`. |
| T-A-05 | `--schema` works for every command that has a `--json-body` option | For each such command, `umbraco <cmd> --schema` exits 0 and prints a JSON Schema document (has `$schema` or `type`/`properties`). Else `contract.schema.<cmd>`. |
| T-A-06 | Global options in `docs/commands.md` match the root options in the catalog | Compare the documented table to `catalog.data.options`. Drift -> `docaccuracy.global-options`. |
| T-A-07 | `getting-started.md` install/auth commands are real | Every `umbraco ...` line there parses under `--help`. Else `docaccuracy.getting-started`. |

The T-A-02/T-A-03 diff is exactly the coverage check the maintainers run; a clean run here means
the docs are complete and non-phantom.

---

## Phase B - Output contract & global flags

Verifies the machine contract every agent depends on. Use `content list` (or any always-present
read) as the probe unless noted.

| ID | Command | Expect |
|---|---|---|
| T-B-01 | `umbraco content list --output json` | exit 0; stdout is JSON; `.status=="success"`; `.data` present; `.meta.command`, `.meta.durationMs`, `.meta.schemaVersion=="2"`. |
| T-B-02 | (redirect stdout to a file, stderr to another) same command | envelope on **stdout**; stderr empty. |
| T-B-03 | `umbraco content get 00000000-0000-0000-0000-000000000000` (bogus id) | non-zero exit; **stderr** carries `{status:"error", code, message}`; stdout has no success envelope. |
| T-B-04 | `umbraco content list` piped (no TTY) | defaults to JSON (not a human table). |
| T-B-05 | `umbraco content list --output csv --take 2` | RFC-4180 CSV; header row uses camelCase keys; not the JSON envelope. |
| T-B-06 | `umbraco content list --fields id,name --output json` | each item has only `id` and `name`, in that order. |
| T-B-07 | `umbraco content list --output csv --fields id,name` | CSV columns are exactly `id,name`. |
| T-B-08 | a delete-style success `--quiet` (defer to Phase E on a test item) | success chatter suppressed; data/errors/exit still emitted. |
| T-B-09 | `NO_COLOR=1 umbraco content list --output human` | no ANSI colour codes in output. |
| T-B-10 | `umbraco content list --verbose` | HTTP request/response logged to **stderr**; envelope still clean on stdout. |
| T-B-11 | Unknown field via `--fields nope` | still exit 0; unknown field simply absent (documented "ignore unknown fields" spirit). Record actual behaviour. |

---

## Phase C - Guardrails

Verifies the agent safety rails. These are high-value: a broken guardrail is a `safety` issue.

| ID | Command | Expect |
|---|---|---|
| T-C-01 | `umbraco content create --document-type clitestDocType --name "x" --readonly` | refused; exit `2`; error says read-only. No entity created. |
| T-C-02 | `UMBRACO_READONLY=1 umbraco content delete <any> ` | refused; exit `2`. |
| T-C-03 | read under readonly: `umbraco content list --readonly` | exit 0 (reads unaffected). |
| T-C-04 | `UMBRACO_ALLOWED_COMMANDS=media umbraco content list` | blocked; exit `2` (content not in allow-list). |
| T-C-05 | `UMBRACO_ALLOWED_COMMANDS=content umbraco content list` | allowed; exit 0. |
| T-C-06 | `UMBRACO_ALLOWED_COMMANDS=media umbraco auth whoami` | allowed; exit 0 (`auth` always allowed). |
| T-C-07 | `UMBRACO_ALLOWED_COMMANDS=" " umbraco content list` | explicit lockdown; blocked; exit `2` (present-but-blank != unset). |
| T-C-08 | `UMBRACO_ALLOWED_COMMANDS=content.list umbraco content get <id>` | blocked; exit `2` (full-name entry allows only `content.list`). |
| T-C-09 | destructive without TTY and without `--yes`: `echo "" | umbraco content delete <test-id>` (piped) | refused; exit `2`; nothing deleted. |
| T-C-10 | `--dry-run` on a write: `umbraco webhook create --url https://x --event x --dry-run` | exit 0; `{status:"dry-run", request:{method,url,body}}`; **no** webhook created (verify via list). |
| T-C-11 | idempotent create: run the same `create --id <fixed>` twice | second run does not create a duplicate (list count unchanged); both exit 0 or the second is a clean no-op/upsert. Record behaviour. |

---

## Phase D - Read-only coverage sweep

For every read command, prove it returns a valid success envelope on v17. This is where v17 API
drift surfaces. Procedure per noun: run `list` (SAFE), capture the first id with
`--fields id`, then run `get <id>`. Where there is no list/get, use the specific command shown.

Run each; expect **exit 0 + success envelope** unless noted. On any non-zero exit or malformed
envelope, record a finding `coverage.<noun>.<verb>` with the stderr message (this catches
"endpoint moved/renamed in v17").

```bash
umbraco content list ; umbraco content get <id>
umbraco content versions <id>
umbraco media list ; umbraco media get <id>
umbraco document-type list ; umbraco document-type get <id|alias>
umbraco media-type list ; umbraco media-type get <id>
umbraco data-type list ; umbraco data-type get <id> ; umbraco data-type is-used <id> ; umbraco data-type referenced-by <id>
umbraco document-blueprint list ; umbraco document-blueprint get <id> ; umbraco document-blueprint scaffold <id>
umbraco language list
umbraco template list ; umbraco template get <alias>
umbraco member list ; umbraco member get <id|email>
umbraco member-type list ; umbraco member-type get <id>
umbraco member-group list ; umbraco member-group get <id>
umbraco user list ; umbraco user get <id|email>
umbraco user-group list ; umbraco user-group get <id>
umbraco user-data list
umbraco dictionary list ; umbraco dictionary get <key>
umbraco webhook list
umbraco script list ; umbraco script get <path>
umbraco stylesheet list ; umbraco partial-view list
umbraco tag list ; umbraco culture list
umbraco server status ; umbraco server info ; umbraco server configuration ; umbraco server troubleshooting
umbraco health list ; umbraco health get <group>
umbraco log-viewer log --take 5 ; umbraco log-viewer levels ; umbraco log-viewer level-count ; umbraco log-viewer message-templates ; umbraco log-viewer saved-search list
umbraco manifest list
umbraco redirect list ; umbraco redirect status
umbraco relation-type list ; umbraco relation-type get <id> ; umbraco relation list --relation-type <relationTypeId>
umbraco indexer list ; umbraco indexer get <name>
umbraco searcher list ; umbraco searcher query <name> --term test
umbraco imaging resize-urls --id <mediaGuid> --width 100
umbraco property-type is-used --document-type <id> --alias <alias>
```

Also verify **help** for every command: `umbraco <cmd> --help` exits 0 and is non-empty
(derive the list from `catalog.json`). A crash or empty help is `contract.help.<cmd>`.

---

## Phase E - Scoped write lifecycles

Full CRUD lifecycles inside the `clitest` namespace, dependency-ordered so each step has its
prerequisites. Every created id comes from the [Appendix](#appendix-fixed-test-ids-and-jq-helpers). Assert the write took effect by a
follow-up read, not just the write's own success envelope.

**E1 Data type -> content type -> content (the core path).**

1. `data-type create --name "clitest DataType" --editor-alias Umbraco.TextBox --editor-ui-alias Umb.PropertyEditorUi.TextBox --id $CLITEST_DATATYPE` -> exit 0.
2. `document-type create --name "clitest DocType" --alias clitestDocType --id $CLITEST_DOCTYPE` (allow at root) -> exit 0. (If root-allow needs a `--json-body`, build it and use `--schema` to validate the body first.)
3. `content create --document-type clitestDocType --name "clitest Root" --id $CLITEST_CONTENT_ROOT` -> exit 0; `content get $CLITEST_CONTENT_ROOT` shows the name.
4. `content create --document-type clitestDocType --name "clitest Child" --parent $CLITEST_CONTENT_ROOT --id $CLITEST_CONTENT_CHILD` -> exit 0; parent is the root.
5. `content update $CLITEST_CONTENT_ROOT --json-body -` (change the name) -> exit 0; a `get` reflects the new name. **If the get shows the old value, that is `content.update.no-op`.**
5a. **Merge (#179).** Give the doc type a second property, set both, then `content update` naming only the first. Read the document back from the Management API (`GET /umbraco/management/api/v1/document/{id}`, since `content get` returns no values - #168). **The second property must still hold its value.** If it is empty, that is `content.update.clobbers-unlisted`.
5b. **Template preservation (#178).** Confirm `template` is still set on that same read-back. **If it is null, that is `content.update.drops-template`** - the bug that 404'd every page in the 2026-09-23 round.
5c. **Replace opt-out.** Repeat 5a with `--replace`; this time the second property *must* be cleared, and the template must *still* be set.
5d. **Template flag (#162).** `content update $CLITEST_CONTENT_ROOT --json-body - --template <alias>` -> the read-back shows that template. An unknown alias must fail with a message naming it, not succeed silently.
6. `content publish $CLITEST_CONTENT_ROOT` -> exit 0 **and the document actually reports published**. Check with `content get` (`isPublished: true`) or a Management API read of `variants[].state`. **Exit 0 alone is not a pass** - the #158 no-op returned exit 0 and `{"status":"success"}` while publishing nothing, for two releases. Then `content unpublish $CLITEST_CONTENT_ROOT --yes` -> exit 0, and confirm it is no longer published.
6a. **Variant publish (#158).** On a culture-varying document, `content publish <id>` with no `--culture` must publish *every* culture, not 400. `"*"` is not a wildcard - it is the invariant culture - so a regression here shows up as `400 "Cannot publish invariant culture when the document varies by culture."`
7. `content versions $CLITEST_CONTENT_ROOT` lists >= 2; `content rollback <versionId>` -> exit 0.
8. `content trash $CLITEST_CONTENT_CHILD` -> exit 0 (in bin); `content restore $CLITEST_CONTENT_CHILD --parent $CLITEST_CONTENT_ROOT` -> exit 0 (back).
9. `content copy $CLITEST_CONTENT_CHILD --parent $CLITEST_CONTENT_ROOT` -> exit 0 (record the copy's id and delete it in teardown).
10. `content move` the copy to root then back -> exit 0.

**E2 Document blueprint.** `document-blueprint from-document $CLITEST_CONTENT_ROOT --name "clitest BP" --id $CLITEST_BLUEPRINT`; then `get`, `scaffold`, `update`, and a folder create/update/delete.

**E3 Media.** `media-type create ... --id $CLITEST_MEDIATYPE`; `media upload ./<small-file> --name "clitest media" --media-type "clitest MediaType" --id $CLITEST_MEDIA`; `get`; `trash`/`restore`; `move`.

**E4 Static files (x3 nouns).** For `script`, `stylesheet`, `partial-view`: `create --name clitest.<ext> --parent clitest --content "..."`; `get <path>` shows the content; `update` changes it; `get` reflects the change.

**E5 Localization.** `language create --culture fr-FR` (pick a culture the site does not
already have; languages are keyed by iso-code, there is no `--id`); `language update fr-FR
--name "clitest French"`; delete in teardown. `dictionary create --key clitest --value
en=Hello --id $CLITEST_DICT`; `get`; delete in teardown.

**E6 Users & groups.** `user-group create --alias clitestGroup --name "clitest Group" --id $CLITEST_USERGROUP`; `add-users`/`remove-users` with an existing user id (read from `user list`); `update`; `delete` in teardown. `user invite` -> record but note it sends an email; prefer a throwaway address or SKIP with `note:"sends-email"` on a shared instance.

**E7 Members.** `member-type create --alias clitestMemberType --name "clitest MT"`; `member create --email clitest@example.com --name "clitest Member" --type clitestMemberType --id $CLITEST_MEMBER`; `update --approved`; `member-group create --name "clitest MG" --id $CLITEST_MEMBERGROUP`.

**E8 Webhooks.** `webhook create --url https://example.com/hook --event "Umbraco.ContentPublish" --name "clitest hook" --id $CLITEST_WEBHOOK`; `list` shows it; delete in teardown.

**E9 user-data.** `user-data create --group clitest --identifier clitest-1 --data "v" --id $CLITEST_USERDATA`; `get`; `update`; delete in teardown.

**E10 log-viewer saved search.** `saved-search create --name clitest-errors --query "@Level='Error'"`; `list` shows it; delete in teardown.

For each step, the ledger row asserts the follow-up read, not just the write. A write that
returns `success` but does not change state is a **major** finding.

---

## Phase F - Schema & content pipelines

The highest-value integration test: export -> diff -> apply round-trips.

**F1 Schema round-trip (read-only proof of idempotency).**

| ID | Command | Expect |
|---|---|---|
| T-F-01 | `umbraco schema export --out $RUN_DIR/schema.json` | exit 0; file is `{schemaVersion: "2", documentTypes[], mediaTypes[], memberTypes[], dataTypes[], templates[]}`; `mediaTypes` and `memberTypes` are non-empty on any stock install (#186). |
| T-F-02 | `umbraco schema diff $RUN_DIR/schema.json` | exit 0; **empty diff** (a fresh export must be in sync with its source). A non-empty diff here is `pipeline.schema.export-diff-drift`. |
| T-F-03 | `umbraco schema export | umbraco schema diff -` | same empty diff via stdin pipe. |
| T-F-04 | `umbraco schema apply $RUN_DIR/schema.json --dry-run` | exit 0; a plan is printed; nothing changes (re-diff still empty). |

**F2 Schema apply (scoped change).** Only `clitestDocType`/`clitestDataType` from Phase E are in
play. Edit the exported snapshot to change a `clitest*` label, `apply` it (create+update; never
prune), then `diff` -> empty; then restore. Do **not** `--prune` a full-site snapshot.

**F3 Content round-trip on the test subtree only.**

| ID | Command | Expect |
|---|---|---|
| T-F-05 | `umbraco content export --root $CLITEST_CONTENT_ROOT --out $RUN_DIR/content.json` | exit 0; `{contentVersion, root, documents[]}` with `root == $CLITEST_CONTENT_ROOT`. |
| T-F-06 | `umbraco content diff $RUN_DIR/content.json` | exit 0; empty (fresh export in sync). |
| T-F-07 | `umbraco content apply $RUN_DIR/content.json --dry-run` | exit 0; plan printed; no change. |
| T-F-08 | scope-safe prune: `content apply $RUN_DIR/content.json --prune --dry-run` | the plan's deletions are **confined to the test subtree** - it must never propose deleting anything outside `$CLITEST_CONTENT_ROOT`. A proposal outside scope is a **blocker** `pipeline.content.prune-scope-leak`. |

Never run `content apply --prune --yes` against a whole-site snapshot on this instance.

---

## Phase G - Bulk operations

On the `clitest` content items only.

| ID | Command | Expect |
|---|---|---|
| T-G-01 | `printf '%s\n%s\n' $CLITEST_CONTENT_ROOT $CLITEST_CONTENT_CHILD | umbraco content bulk publish` | exit 0; `data` is a per-item results array `{id,status,error}`; both `status` ok. |
| T-G-02 | include one bogus id in the stdin list | exit `1` (any item failed); the good ids still report their own status; the bogus one reports its error. |
| T-G-03 | `... | umbraco content bulk unpublish --yes` | exit 0; per-item results. |
| T-G-04 | `... | umbraco content bulk delete --yes` (test ids only) | exit 0; per-item results; a single confirmation, never per-item. (This doubles as part of teardown - only ever the test ids.) |
| T-G-05 | pipe from a query: `umbraco content list --parent $CLITEST_CONTENT_ROOT --fields id | jq -r '.data[].id' | umbraco content bulk publish` | exit 0; documents the documented piping recipe end to end. |

---

## Phase H - DANGER / opt-in (default SKIP)

Only if `RUN_DANGEROUS=1` **and** a human confirmed the instance is disposable. Otherwise write
`SKIP` rows with `note:"danger-optin"`.

| ID | Command | Guard |
|---|---|---|
| T-H-01 | `umbraco health run <group> --yes` | POST; low risk but may have side effects. Record results. |
| T-H-02 | `umbraco indexer rebuild <name> --yes` | expensive; degrades search while running. |
| T-H-03 | `umbraco models-builder status` then `models-builder build --yes` | regenerates server source; only where that is acceptable. |
| T-H-04 | `redirect status` -> `redirect tracking disable --yes` -> `redirect status` -> **restore** to original (`enable`/`disable`) -> `redirect status` | site-wide toggle; you MUST restore the original value. |
| T-H-05 | `content empty-recycle-bin` / `media empty-recycle-bin` | default SKIP even here unless the bin is known to hold only your test items. |

---

## Appendix: fixed test ids and jq helpers

Reuse these exact GUIDs on every run (they are valid v4 GUIDs). Set them once in preflight.

```bash
export CLITEST_DATATYPE="11111111-1111-4111-8111-111111111111"
export CLITEST_DOCTYPE="22222222-2222-4222-8222-222222222222"
export CLITEST_MEDIATYPE="33333333-3333-4333-8333-333333333333"
export CLITEST_CONTENT_ROOT="a1a1a1a1-0000-4000-8000-000000000001"
export CLITEST_CONTENT_CHILD="a1a1a1a1-0000-4000-8000-000000000002"
export CLITEST_BLUEPRINT="a1a1a1a1-0000-4000-8000-000000000003"
export CLITEST_USERGROUP="a1a1a1a1-0000-4000-8000-000000000004"
export CLITEST_MEMBER="a1a1a1a1-0000-4000-8000-000000000005"
export CLITEST_MEMBERGROUP="a1a1a1a1-0000-4000-8000-000000000006"
export CLITEST_MEDIA="a1a1a1a1-0000-4000-8000-000000000007"
export CLITEST_DICT="a1a1a1a1-0000-4000-8000-000000000008"
export CLITEST_WEBHOOK="a1a1a1a1-0000-4000-8000-000000000009"
export CLITEST_USERDATA="a1a1a1a1-0000-4000-8000-00000000000a"
export CLITEST_LANG="fr-FR"   # languages are keyed by iso-code, not a guid
```

jq helpers:

```bash
# Assert a success envelope; exit non-zero if not.
assert_success(){ jq -e '.status=="success" and .meta.schemaVersion=="2"' >/dev/null; }
# First id from a list result.
first_id(){ jq -r '.data[0].id // .data[0].key // empty'; }
# Strip volatile fields before comparing outputs across runs.
NORMALIZE='del(.meta.durationMs, .meta.timestamp)'
```

Ledger row emitter (bash):

```bash
row(){ jq -cn --arg id "$1" --arg ph "$2" --arg t "$3" --arg cmd "$4" \
  --arg ex "$5" --arg st "$6" --argjson rc "$7" --arg fk "${8:-}" --arg sv "${9:-}" --arg n "${10:-}" \
  '{id:$id,phase:$ph,title:$t,cmd:$cmd,expect:$ex,status:$st,exitCode:$rc,
    findingKey:($fk|select(.!="")),severity:($sv|select(.!="")),note:$n}' >> "$RUN_DIR/ledger.jsonl"; }
```

---

### Notes on determinism, restated

- Same CLI build + same instance baseline => same per-`id` `status` in `ledger.jsonl`. That is
  the re-run guarantee. Timestamps, `durationMs`, and any server ids where `--id` was not honoured
  will differ and are excluded from comparison by design.
- Teardown returning the instance to baseline is what makes run N+1 start where run N started.
- A fix should move exactly its target test from `FAIL` to `PASS`; if unrelated tests move,
  that itself is worth a finding.
