# Hands-on test harness

End-to-end testing of the `umbraco` CLI against **real, throwaway Umbraco sites** on your machine. One command creates
two sites (a source and a staging target), builds a small multilingual site on the source with the CLI alone, and checks
the front end and forms. A test plan (T1–T10) then covers every command group, including promotion between the sites.
It's written to be run by an AI agent, with a human reviewing the results.

It complements [`docs/testing/alpha-test-guide.md`](../../docs/testing/alpha-test-guide.md), which runs safely against an
existing instance. This harness creates and deletes its own sites, so it can test destructive commands too.

## Requirements

- macOS, Linux or Windows. The scripts are Python (standard library only), so they run the same on all three.
- Python 3.9+. On Windows, type `python` (or `py`) wherever these docs say `python3`.
- .NET SDK 10+ (Umbraco 17) **and** the .NET 9 runtime (the CLI targets net9.0)
- A trusted HTTPS dev certificate: `dotnet dev-certs https --trust`. On Linux, `--trust` isn't supported everywhere: see
  [Microsoft's Linux instructions](https://learn.microsoft.com/aspnet/core/security/enforcing-ssl#trust-https-certificate-on-linux).
- `git`; `gh` (authenticated) only for re-tests and filing issues
- Internet access to nuget.org; about 1 GB of disk per site
- Only for the hand-run snippets in `TEST-PLAN.md`: `bash`, `jq` and `curl`. On Windows that means Git Bash plus
  `winget install jqlang.jq` (curl ships with Windows).

`python3 preflight.py` checks all of this.

## Quick start

```bash
cd tests/hands-on
python3 preflight.py
python3 setup-round.py        # packs the CLI from this checkout, creates sites/source + sites/staging, builds Part A
```

Other CLI builds: `python3 setup-round.py --nupkg path/to/Umbraco.Community.Cli.<ver>.nupkg` or `--nuget <version|latest>`.
Other Umbraco versions: `--umbraco 17.6.0` (or a major, or `latest`).

Then use a site's CLI directly, from anywhere:

```bash
sites/source/umb content list -o human       # macOS, Linux, Git Bash
sites\source\umb.cmd auth doctor              # Windows PowerShell or cmd
```

The harness keeps its CLI profiles in `tests/hands-on/.cli/config.json`. Your own `umbraco` profiles are never read or changed.

## Handing a round to an agent

Open an agent (Claude Code or similar) at the repository root and give it something like:

> Read `tests/hands-on/AGENTS.md` and run a hands-on test round of this checkout's CLI
> (or: of `~/Downloads/Umbraco.Community.Cli.0.1.0-alpha.14.nupkg`). Log findings in `tests/hands-on/LEDGER.md`
> as you go, report back with a summary, and don't file issues or remove the sites until I say so.

[`AGENTS.md`](AGENTS.md) holds the operating contract, safety rules, steps, gotchas and troubleshooting.
[`TEST-PLAN.md`](TEST-PLAN.md) says what to test and what counts as a pass. [`LEDGER.md`](LEDGER.md) holds the findings, and
`tools/file_issues.py` files them as GitHub issues when you ask.

## The dev site (live integration tests)

`dev-site.py` keeps one persistent site, `sites/dev`, for day-to-day development: the live integration suite
(`tests/Umbraco.Cli.IntegrationTests`) and `scripts/fetch-spec.ps1` use it instead of a hand-built instance.

```bash
python3 dev-site.py test                  # create sites/dev on first use (Part A fixture content), start it, run the suite
python3 dev-site.py test --filter "FullyQualifiedName~CommandIntegrationTests"   # extra options go to dotnet test
python3 dev-site.py up | down             # start / stop it
python3 dev-site.py env [--shell pwsh]    # host, API-user credentials and UMBRACO_TEST_CONFIG
python3 dev-site.py reset --umbraco 17.3.5   # rebuild it, optionally on another Umbraco version
```

It has its own CLI config, `sites/dev/test-config.json` (a single default profile, `dev`). `test` points the suite at it through
`UMBRACO_TEST_CONFIG` and clears every other `UMBRACO_*` variable for the run, so your own profiles and shell settings are never
used. Rounds don't touch it: `setup-round.py --replace` only removes `source` and `staging`.

## Benchmarks (`bench.py`)

`bench.py` times real commands end to end with [hyperfine](https://github.com/sharkdp/hyperfine) against the dev site,
for each CLI build and Umbraco version you name, and writes the results to
[`docs/performance/results/`](../../docs/performance/results/). It answers two questions: did a CLI build get slower,
and how does one Umbraco version compare with another? Like `dev-site.py test`, it runs locally only, because CI has
no live Umbraco (#139, #77).

It needs `hyperfine` on top of the requirements above. Without it, `bench.py` prints how to install it and runs
nothing: `winget install --id sharkdp.hyperfine -e` (Windows), `brew install hyperfine` (macOS), `apt install
hyperfine` or `cargo install hyperfine` (Linux). If it isn't on `PATH`, pass `--hyperfine <path>` or set
`HYPERFINE`.

```bash
python3 bench.py --cli local --umbraco 17                          # this checkout on the latest Umbraco 17
python3 bench.py --cli 0.1.0-alpha.6,local --umbraco 17,18         # a published build against this checkout, on 17 and 18
python3 bench.py --scenario get,list --runs 20 --out-dir .cache/bench/scratch   # a quick look, not for committing
python3 bench.py --help                                            # every option
```

- **CLI builds** (`--cli`, comma-separated): `local` packs this checkout with `pack-cli.py`. A version (or `latest`)
  installs from nuget.org, plus the repo's `./nupkg` and `tests/hands-on/nupkg` when they exist (so a release build
  packed with `dotnet pack -o ./nupkg` but not yet pushed installs too), plus any `--source`. Each build is installed
  side by side in `.cache/bench/cli/<version>` with `dotnet tool install --tool-path` and run through its `umbraco`
  shim, the way an installed tool runs.
- **Umbraco versions** (`--umbraco`, comma-separated: a major, an exact version or `latest`): the dev site is started
  with `dev-site.py up` when it is already on that exact version, and rebuilt with `dev-site.py reset --umbraco <ver>`
  when it isn't, so every run times the same fixture content. `--reset` rebuilds it anyway (for example after
  `dev-site.py test` has left data behind). A reset deletes the dev site, and the site stays on the last version run.
- **Isolation:** the CLI runs with every `UMBRACO_*` variable from your shell cleared, the dev site's API user in
  `UMBRACO_HOST` / `UMBRACO_CLIENT_ID` / `UMBRACO_CLIENT_SECRET`, and `--config` naming a file that doesn't exist, so
  your own profiles and allow-lists are never read. The token cache is the normal per-user one.

### Scenarios

The defaults are the scenarios #410 names. They live in the `SCENARIOS` table at the top of `bench.py`, so changing
what is timed is a one-line edit; `--scenario` picks a subset for one run.

| Scenario | Command | What it exercises |
|---|---|---|
| `version` | `umbraco --version` | Process start and the command tree; no config, no HTTP |
| `get` | `umbraco content get <home page>` | One item, with its follow-up reads (type, ancestors, URLs) |
| `list` | `umbraco data-type list --all` | A large list: every data type on the site, each read by id |
| `content-export` | `umbraco content export --out <file>` | The whole fixture content tree to a snapshot |
| `schema-diff` | `umbraco schema diff fixtures/schema.json` | The fixture schema snapshot against the site |

The API scenarios also pass `--output json`. A scenario that fails on a build (usually an older build without that
command or snapshot format) is recorded as failed with the CLI's error, and the rest still run; `bench.py` then exits 1.

### Method: end-to-end time and CLI overhead

Each result separates what the CLI costs from what Umbraco costs, so an Umbraco speed-up or slow-down is not credited
to the CLI:

1. **Smoke run.** One run with `--verbose`. It checks the command works on this build and warms the token cache.
2. **End-to-end time.** hyperfine runs the command `--warmup` times (default 3) untimed, then `--runs` times (default
   10) timed, with no shell (`-N`) and output discarded. `endToEndMs` is hyperfine's statistics for those runs.
3. **HTTP time.** `--http-runs` (default 5) separate `--verbose` runs, never timed by hyperfine, so the logging can't
   skew the end-to-end numbers. `VerboseHttpHandler` logs each response as `< 200 OK (12 ms)`, timed from sending the
   request to receiving its response headers. `bench.py` timestamps each of those lines as it arrives, which makes
   each request the interval `[arrival - duration, arrival]`. The HTTP time is the length of the union of those
   intervals, so requests the CLI sends concurrently (such as `data-type list` reading data types in batches of 8)
   count once. The logged durations are truncated to whole milliseconds, so 0.5 ms is added to each.
4. **CLI overhead** = end-to-end mean minus HTTP mean. It covers process start, config and auth, parsing, our own
   CPU, writing output, and reading response bodies, which the handler's clock doesn't include. On localhost that
   last part is small.

HTTP time is everything inside the HTTP stack, not only Umbraco's server time. In particular the first request of
each process opens the connection (TCP and the TLS handshake) and pays for the first use of .NET's HTTP stack, which
on the baseline machine is about 50 ms of that request.

Only requests a build logs are counted. Builds before 0.1.0-alpha.15 don't log the OAuth token exchange under
`--verbose`, and builds before alpha.14 have no token cache and exchange a token on every run. For those builds (such
as alpha.6) the token round trip is counted as CLI overhead.

hyperfine's standard deviation is the spread to compare against: a rerun on a quiet machine should land within it.
Other load on the machine (builds, test runs, other benchmarks) widens it, so close what you can before a run that
you mean to commit.

### Results files

Every run writes `docs/performance/results/<YYYY-MM-DD>_<HHMMSS>_<machine>.json` and a markdown summary with the same
name and `.md`, which is also printed. The time is the run's start in UTC. `<machine>` is `--machine`, or a label
made from the OS and CPU model (such as `windows-amd-ryzen-9-7950x`). The results are committed, so the history lives
in the repo (#410). hyperfine's own exports stay in `.cache/bench/runs/`, which git ignores.

The JSON is what other tools read (the generated README section, #411). One file is one run on one machine:

| Field | Meaning |
|---|---|
| `schemaVersion` | `1`. Raised when a field changes meaning or is removed; adding a field doesn't raise it |
| `date` | When the run started, UTC, ISO 8601 (`2026-09-29T10:07:46Z`) |
| `machine` | `label` (as in the file name), `cpu`, `logicalCores`, `memoryGiB`, `os`, `arch` |
| `hyperfine` | `version`, and the `warmup` and `runs` per command |
| `httpRuns` | `--verbose` runs per result for the HTTP time |
| `results[]` | One row per Umbraco version x CLI build x scenario, in run order |

Each row in `results`:

| Field | Meaning |
|---|---|
| `scenario` | The scenario name (`version`, `get`, `list`, `content-export`, `schema-diff`) |
| `command` | The command, with placeholders (`{home}`, `{config}`) instead of ids and paths |
| `cli` | `label` (as given to `--cli`, such as `local`), `version` (the package version), `commit` (for `local` builds: the checkout's commit, `-dirty` when `src/` had changes; otherwise `null`), `dotnet` (the .NET runtime it ran on) |
| `umbraco` | The exact Umbraco version |
| `status` | `ok`, or `failed` with an `error` and no timings |
| `endToEndMs` | hyperfine's `mean`, `stddev`, `median`, `min` and `max`, and every timed run in `times`, in ms |
| `httpMs` | Mean HTTP time per run in ms: `0` for a command that makes no requests, `null` if the build logged requests without timings |
| `cliOverheadMs` | `endToEndMs.mean` minus `httpMs`, or `null` when `httpMs` is |
| `requests` | Requests per run, as logged by `--verbose` |

A row's full stamp is its own `cli` and `umbraco` plus the file's `machine` and `date`.

## Single sites

```bash
python3 new-site.py --help                                        # any Umbraco version × any CLI build
python3 new-site.py --name v18-smoke --umbraco 18 --cli latest    # a published CLI on Umbraco 18
python3 pack-cli.py                                               # pack this checkout; prints the version last
python3 new-site.py --name local --cli-source nupkg --cli <that version>
python3 remove-site.py --list                                     # all sites, versions, ports, running or not
python3 remove-site.py <name>                                     # stop, remove its profile, delete the folder
python3 sites/<name>/stop.py; python3 sites/<name>/start.py       # logs in sites/<name>/logs/site.log
```

Each site has an admin (`admin@example.com` / `Password1234!`) and an API user whose client credentials are in
`sites/<name>/credentials.json`. These sites are local and disposable, so plaintext is fine.
