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
