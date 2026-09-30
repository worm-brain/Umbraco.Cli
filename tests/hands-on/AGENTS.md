# Hands-on test harness: agent runbook

You are the **test agent** for a hands-on round of the `umbraco` CLI (Umbraco.Community.Cli). This folder creates
**throwaway Umbraco sites on this machine**, builds a real multilingual site on them with the CLI alone, promotes it to a
second site, and works through every command group. You record what you find in `LEDGER.md`, a local working file.

> This harness creates and destroys its own sites, so destructive tests are fine here, within the rules below. If you are
> editing the CLI's code rather than testing it, read the repo root [`AGENTS.md`](../../AGENTS.md).

Work from this folder: `cd tests/hands-on`. Every path below is relative to it.

**Platforms.** The harness scripts are Python and run the same on macOS, Linux and Windows. On Windows, type `python`
where this file says `python3`, and use `sites\<s>\umb.cmd` from PowerShell or cmd. The hand-run snippets here and in
`TEST-PLAN.md` are bash with `jq` and `curl`: on Windows run them in Git Bash (with `jq` installed), where `sites/<s>/umb`
works as written.

---

## Operating contract

1. **Preflight** (Step 0). If it fails, stop and tell the human exactly what to install. Don't work around it.
2. **Set up the round** (Step 1) with the CLI build the human asked for. The default is this checkout (`--from-repo`).
3. **Open the round in `LEDGER.md`** (Step 2) before testing anything.
4. **Re-test** the previous round's issues (Step 3), then run **Part B, T1–T10** from [`TEST-PLAN.md`](TEST-PLAN.md) (Step 4).
   Part A was already run by the setup script: check its output rather than repeating it.
5. **Log every finding in `LEDGER.md` the moment you confirm it** (Step 5). Don't batch them up for the end.
6. **Write up** (Step 6): the re-test and test-log tables, the Known lines in `TEST-PLAN.md`, and a short report to the human.
7. **Don't file GitHub issues, comment on issues, or remove sites unless the human asks.** When asked, follow Step 7 or Step 8.

A round takes 1–3 hours of agent time. The slow parts are site creation (~2–5 min per site, more on the first run while
NuGet restores) and T5, which waits for Umbraco's scheduler.

---

## Hard rules

- **Only touch sites under `sites/`.** Never point the harness at a real Umbraco instance, and never pass `--host` for one.
- **Never run `auth login` / `auth logout` through a site wrapper without `--config <temp file>`.** The wrappers use the
  harness config (`.cli/config.json`); auth tests belong in an isolated config (TEST-PLAN T10). The user's own `umbraco`
  config is never used by the harness. Keep it that way.
- **Destructive tests** (in-use deletes, prunes, `--force`, `--replace`, direct API `PUT`s) **run on `sites/staging`.** The source is the
  reference: if staging breaks, re-promote from the source (T1).
- **Verify effects, not exit codes.** Re-read with `get`, `curl` the page, or query SQLite before calling anything a pass or a bug.
- **Leaving the CLI is a finding.** If you need the Management API, SQL or site code to finish a step, log it with the workaround.
- **Don't edit the CLI's source** during a round. You are measuring a build, not fixing it.

---

## Step 0 · Preflight

```bash
python3 preflight.py
```

It checks: Python 3.9+, git, .NET SDK 10+, the .NET 9 runtime (the CLI targets net9.0), the ASP.NET Core 10 runtime, a
trusted HTTPS dev certificate, access to nuget.org, and free ports 44800+. Every line must be `pass` (`note` is informational:
it flags a missing `bash`, `jq`, `curl` or `gh`, which the snippets and re-tests need). Typical fixes: `dotnet dev-certs https --trust`; install the .NET 9 runtime alongside SDK 10.

## Step 1 · Set up the round

```bash
python3 setup-round.py                             # CLI packed from this checkout (default)
python3 setup-round.py --nupkg ~/Downloads/Umbraco.Community.Cli.0.1.0-alpha.14.nupkg   # a build the human handed you
python3 setup-round.py --nuget latest              # a published version
python3 setup-round.py --replace …                 # remove last round's 'source' / 'staging' first
```

It runs preflight, gets the CLI build, creates `sites/source` (port 44800 if free) and `sites/staging` (the next free port),
runs Part A (`tools/build_site.py`), installs the form controller, runs the 8 form checks, and writes `round.env` (date, CLI
version and commit, Umbraco version, site hosts).

**Success looks like:** `All steps passed` from the build, no `SLOW` lines, `8/8 checks passed`, and `Part A build: PASS  Forms: PASS`
at the end. A `FAIL` line in Part A is a candidate finding. Reproduce it by hand (the TEST-PLAN Part A table says how) before logging it.

Each site folder has `umb` (the CLI pinned to that site: `sites/source/umb content list`; `umb.cmd` too on Windows),
`start.py`, `stop.py`, `site.env`, `credentials.json` (admin `admin@example.com` / `Password1234!`, and the API user's client
id/secret) and `logs/site.log`. `python3 remove-site.py --list` shows every site and whether it's running.

## Step 2 · Open the round in `LEDGER.md`

`LEDGER.md` is git-ignored. If it doesn't exist, copy `LEDGER.template.md` to `LEDGER.md`. Append a `# Round N` section using
the format at the top: the heading, the `<!-- round: … -->` line (values from `round.env`, and a unique `label`), then empty
re-test, test-log and findings tables. N is the previous round's number plus one. Finding ids continue from one past the
highest id in `LEDGER.md` or `ledger-history.json`, whichever is higher.

## Step 3 · Re-test the previous round

```bash
gh issue list -R worm-brain/Umbraco.Cli --label cli-testing --state all --limit 100 --json number,title,state,labels \
  --jq '.[] | "\(.number) \(.state) \([.labels[].name|select(startswith("test-round"))]|join(",")) \(.title)"'
```

Re-test every issue from the most recent round (all of them, open or closed) and any still-open `cli-testing` issue from earlier
rounds. For each: read the issue (`gh issue view <n> -R worm-brain/Umbraco.Cli --comments`, whose closing comment says what the
fix was meant to do), run its repro on the new build, and write one re-test row: ✅ fixed / 🟡 partly / ❌ still failing, plus the evidence.

Also check what changed in the command surface since the last round:

```bash
sites/source/umb commands > sites/source/work/surface-now.json  # the whole command tree
git log --oneline <previous round's commit>..HEAD -- src   # when testing this checkout
```

New or renamed commands and options need testing even if no issue mentions them.

## Step 4 · Part B (T1–T10)

Follow [`TEST-PLAN.md`](TEST-PLAN.md) Part B. Use the conventions block at its top (`S`, `U`, `T`, `IDS`, `id()`).

- **Order:** T1 first (it fills staging, which T8 and later destructive checks rely on). T5 has two ~2 minute waits: start it,
  do other tests, come back. T10's auth checks use an isolated `--config`.
- **Subagents:** T2–T9 are independent once T1 is done. When fanning out, give each subagent this file, `TEST-PLAN.md` and its
  test, and tell it to **return** findings rather than edit `LEDGER.md` (one writer avoids clobbered edits).
- **Compare, don't guess:** `python3 tools/compare_sites.py <portA> <portB>` diffs every page of two sites. Use it after T1 and whenever
  a change should (or shouldn't) be visible on the front end.

## Step 5 · Logging findings

For each problem:

1. **Reproduce it** in the smallest form (one or two commands), and check it's the CLI rather than your test data or Umbraco.
   Compare with the Management API directly when unsure (recipe under Troubleshooting).
2. **Check for duplicates:** `gh issue list -R worm-brain/Umbraco.Cli --state open --search "<keywords>"`. If one exists, put
   `covered by #N` in the Issue column instead of leaving it empty.
3. **Write it** in `LEDGER.md` straight away: a findings-table row plus a `### L-NNN` entry (format at the top of the ledger).
   Quote exact commands and output. Say what you expected and why (help text, docs, how other commands behave).
4. **Umbraco's own behaviour** (not the CLI's) goes in `TEST-PLAN.md` as a Known note, not in the findings table, unless the CLI
   could reasonably handle it better (then it's an Improvement).

Severity: 🔴 data loss, or a headline feature (build, promote) blocked. 🟠 wrong result, misleading success, or a common task
impossible without leaving the CLI. 🟡 everything else.

## Step 6 · Write up

- Fill in the round's re-test table and test log in `LEDGER.md`.
- In `TEST-PLAN.md`, update each test's **Known** line to the new build and add a row to the History table.
- Report to the human: the build tested, the counts (re-tests ✅/🟡/❌, tests passed), each new finding in one line (worst first), and
  anything you couldn't finish. Offer to file the findings. Don't file them unasked.

## Step 7 · Filing (only when asked)

```bash
python3 tools/file_issues.py --round N --dry-run      # review titles and labels
python3 tools/file_issues.py --round N                # file; writes issue numbers into LEDGER.md
python3 tools/file_issues.py --round N --epic summary.md   # optional: also open a tracking issue (summary.md = its opening section)
```

It needs `gh` authenticated with write access to `worm-brain/Umbraco.Cli`, and creates the round's label if missing. Then comment
on the previous round's tracking issue with this round's re-test table.

## Step 8 · Teardown (only when asked)

```bash
python3 remove-site.py source && python3 remove-site.py staging    # stops the site, removes its profile from the harness config, deletes the folder
```

Keep the sites until the human has read the report: they're the evidence.

---

## Gotchas (learned the hard way)

- **Scripts run from `tests/hands-on`.** `sites/…` paths are relative to it.
- **zsh:** `path` is tied to `$PATH`, so never use it as a variable name. Unquoted variables don't word-split: use `${=var}` or run
  loops in bash. **macOS bash is 3.x** (no `mapfile`, no associative arrays); Python is simpler for anything non-trivial.
- **`jq -r .data.content > file`** adds a trailing newline. Byte-exact copies need `jq -j`.
- **A direct `PUT /document/{id}` replaces every value.** The CLI's `content update` merges; use `--replace` only on purpose.
- **Label properties** (`Umbraco.Label`) can't be written through the Management API; promotion skips them with a warning.
- **Scheduler and webhooks are asynchronous:** schedules fire within ~60 s of their time, and webhook deliveries arrive a few seconds late.
- **Razor:** don't name a loop variable `page` (`@page` is a directive). View compile errors show only as `UmbracoCompilationException`;
  bisect by swapping the partial's content with `partial-view update --content`.
- **Culture-variant content:** `publish` with no `--culture` publishes every culture; a document with only en-US can't be
  published with `--culture en-US,da-DK`. A child can't be published before its parent.
- **Version ids look like `0000003e-0000-…`.** That's Umbraco's int-to-GUID encoding, not a bug.
- **Git Bash rewrites leading-slash arguments** (`/site.css` becomes `C:/Program Files/Git/site.css`), so file paths 404. Set
  `export MSYS_NO_PATHCONV=1`. **On Windows, `jq -r`/`-j` write CRLF**, so strip `\r` before re-uploading a file or using a URL list.
  `winget install jqlang.jq` if `jq` is missing (it lands in `%LOCALAPPDATA%\Microsoft\WinGet\Links`).
- **Parallel lanes share one SQLite site.** Five agents on `sites/source` at once hung Umbraco for ~10 min on `database table is locked`
  (round 5). Keep heavy writes on different sites (a third site from `new-site.py --no-default` is cheap), and don't read a stall as a CLI bug.

## Troubleshooting

| Symptom | Fix |
|---|---|
| A site won't start | `tail -50 sites/<s>/logs/site.log`; `python3 sites/<s>/stop.py` stops it and anything left on its port |
| `BootFailed` / 500 on every page | Umbraco's log: `sites/<s>/UmbracoSite/umbraco/Logs/UmbracoTraceLog.*.json` (one JSON event per line; `jq 'select(."@l"=="Error")'`) |
| CLI TLS errors | `dotnet dev-certs https --trust`, then restart the site |
| `setup-round.py` says a site exists | `--replace`, or `python3 remove-site.py source` |
| An old CLI build seems to run | `pack-cli.py` versions are unique per run; for `--nupkg`, `new-site.py` clears that version from the NuGet cache first |
| The front end differs after a change | `tools/compare_sites.py` against a site that doesn't have the change |

**Direct Management API** (for verification only, never as the test itself):

```bash
C=sites/source/credentials.json; H=$(jq -r .host $C)
TOK=$(curl -sk -d grant_type=client_credentials -d client_id=$(jq -r .apiUser.clientId $C) -d client_secret=$(jq -r .apiUser.clientSecret $C) \
  $H/umbraco/management/api/v1/security/back-office/token | jq -r .access_token)
curl -sk -H "Authorization: Bearer $TOK" $H/umbraco/management/api/v1/document/<id> | jq .
```

---

## What's in this folder

| Path | What |
|---|---|
| `preflight.py`, `setup-round.py` | Check the machine; set up a whole round in one command |
| `new-site.py`, `remove-site.py` | Create / remove one throwaway site (`--help` for options; `remove-site.py --list`) |
| `dev-site.py` | The persistent `sites/dev` for the live integration suite (`up`, `down`, `test`, `env`, `reset`). Not part of a round: leave it alone |
| `pack-cli.py` | Pack this checkout's CLI into `nupkg/` with a unique `0.1.0-local.<timestamp>` version |
| `bench.py` | hyperfine timings per CLI build x Umbraco version on `sites/dev` (can reset it); results in `docs/performance/results/`. `bench.py report` regenerates the README's Performance section and `docs/performance.md` from them. Not part of a round |
| `TEST-PLAN.md` | What to test: Part A (scripted) and T1–T10, pass criteria, Known issues per test |
| `LEDGER.template.md`, `ledger-history.json` | The ledger's format (copy to the git-ignored `LEDGER.md` for a round); every filed id → its issue number |
| `tools/build_site.py` | Part A: rebuild the fixture site with the CLI (ok/FAIL per step, timings) |
| `tools/compare_sites.py` | Page-by-page diff of two sites |
| `tools/submit_forms.py`, `tools/install_controller.py` | Contact + sign-up form checks (8); install the form controller (site code) |
| `tools/safety_matrix.py` | T10: 23 guardrail and exit-code cases |
| `tools/webhook_listener.py` | T3: local webhook receiver |
| `tools/file_issues.py` | File unfiled ledger rows as GitHub issues (+ optional tracking issue) |
| `tools/harness.py` | Shared cross-platform plumbing: sites, the pinned CLI, start/stop, ports, HTTP |
| `tools/perf_report.py` | `bench.py report`: renders the README's Performance block and `docs/performance.md` (and holds their prose) |
| `fixtures/` | `schema.json` + `content.json` (exports of the finished test site), images, PDFs |
| `assets/` | Header/footer/block partials, site.css/js, and the contact form controller (C#, site code) |
| `sites/`, `.cli/`, `nupkg/`, `round.env` | Created by a round; git-ignored |
