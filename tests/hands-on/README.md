# Hands-on test harness

End-to-end testing of the `umbraco` CLI against **real, throwaway Umbraco sites** on your machine. One command creates
two sites (a source and a staging target), builds a small multilingual site on the source with the CLI alone, and checks
the front end and forms. A test plan (T1–T10) then covers every command group, including promotion between the sites.
It's written to be run by an AI agent, with a human reviewing the results.

It complements [`docs/testing/alpha-test-guide.md`](../../docs/testing/alpha-test-guide.md), which runs safely against an
existing instance. This harness creates and deletes its own sites, so it can test destructive commands too.

## Requirements

- macOS or Linux (WSL works; plain Windows / Git Bash doesn't, since the scripts need `bash` and `lsof`)
- .NET SDK 10+ (Umbraco 17) **and** the .NET 9 runtime (the CLI targets net9.0)
- A trusted HTTPS dev certificate: `dotnet dev-certs https --trust`. On Linux, `--trust` isn't supported everywhere: see
  [Microsoft's Linux instructions](https://learn.microsoft.com/aspnet/core/security/enforcing-ssl#trust-https-certificate-on-linux).
- `curl`, `jq`, `openssl`, `lsof`, `python3`, `git`; `gh` (authenticated) only for filing issues
- Internet access to nuget.org; about 1 GB of disk per site

`./preflight.sh` checks all of this.

## Quick start

```bash
cd tests/hands-on
./preflight.sh
./setup-round.sh              # packs the CLI from this checkout, creates sites/source + sites/staging, builds Part A
```

Other CLI builds: `./setup-round.sh --nupkg path/to/Umbraco.Community.Cli.<ver>.nupkg` or `--nuget <version|latest>`.
Other Umbraco versions: `--umbraco 17.6.0` (or a major, or `latest`).

Then use a site's CLI directly, from anywhere:

```bash
sites/source/umb content list -o human
sites/source/umb auth doctor
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

## Single sites

```bash
./new-site.sh --help                                  # any Umbraco version × any CLI build
./new-site.sh --name v18-smoke --umbraco 18 --cli latest        # a published CLI on Umbraco 18
./new-site.sh --name local --cli-source nupkg --cli "$(./pack-cli.sh | tail -1)"   # this checkout's CLI
./remove-site.sh --list                               # all sites, versions, ports, running or not
./remove-site.sh <name>                               # stop, remove its profile, delete the folder
sites/<name>/stop.sh; sites/<name>/start.sh           # logs in sites/<name>/logs/site.log
```

Each site has an admin (`admin@example.com` / `Password1234!`) and an API user whose client credentials are in
`sites/<name>/credentials.json`. These sites are local and disposable, so plaintext is fine.
