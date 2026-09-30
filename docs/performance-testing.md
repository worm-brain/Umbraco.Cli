# Performance testing

How the CLI's speed is measured, how to read the numbers on the [performance page](performance.md), and how to
run the tests yourself. Every command runs from the repository root; on Windows, type `python` for `python3`.

## Three layers

The CLI wraps Umbraco's Management API, so most of a command's time is Umbraco's. The parts the CLI owns are
startup, config and login, how many requests it makes, and its own work: parsing, writing output, and the
export, diff and apply engines. Three layers cover those, each where it can measure reliably:

| Layer | Measures | Runs | Can fail a build? |
|---|---|---|---|
| [Request counts](../tests/Umbraco.Cli.Tests/RequestCounts/) | HTTP requests per command, against a fake Umbraco that records every request | `dotnet test`, on every pull request | Yes: exact counts |
| [BenchmarkDotNet](../tests/Umbraco.Cli.Benchmarks/README.md) | CPU time and allocations of the CLI's own code, on in-memory fixtures | CI: pull requests labelled `performance`, weekly on `main`, on demand | Allocations: yes, more than 10% over `main`. Timings: no |
| [hyperfine](../tests/hands-on/README.md#benchmarks-benchpy) | End-to-end time per CLI version and Umbraco version | Locally, against the test harness's dev site | No: it reports |

### Request counts

The request-count tests run the real command tree, client and login with only the network swapped for a fake
Umbraco. Each test pins how many requests a command makes, as a formula where the count depends on the data (for
example `ceil(n / 100)` pages for `content tree` with n items at the root).

They catch a command that starts making one request per item, a lost cache, or paging that asks for more pages
than it needs. They can't see how long a request takes.

```bash
dotnet test tests/Umbraco.Cli.Tests --filter "FullyQualifiedName~RequestCount"
```

They need no Umbraco, and they run as part of every `dotnet test`.

### BenchmarkDotNet

[`tests/Umbraco.Cli.Benchmarks`](../tests/Umbraco.Cli.Benchmarks/README.md) measures startup wiring, the output
writers, response parsing, and the snapshot and diff engines on fixed in-memory data. The
[Benchmarks workflow](../.github/workflows/benchmarks.yml) runs a pull request and `main` on the same runner and
fails when a benchmark allocates more than 10% more per operation than `main`. Timings show in the job summary
but never fail the build.

```bash
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*" --job short
```

To compare two builds the way the CI gate does:

```bash
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*" --job short --artifacts ./bench/before
# ...make the change...
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*" --job short --artifacts ./bench/after
pwsh ./scripts/ci/benchmark-compare.ps1 -Baseline ./bench/before -Candidate ./bench/after -BaselineLabel before -CandidateLabel after
```

### hyperfine end to end

[`tests/hands-on/bench.py`](../tests/hands-on/README.md#benchmarks-benchpy) times real commands with
[hyperfine](https://github.com/sharkdp/hyperfine) against the test harness's dev site, a local Umbraco with the
harness's fixture content. Each CLI version is installed side by side and run the way an installed tool runs.
These are the numbers on the performance page.

For each command it records:

- **End-to-end time** - what you wait for: a few warm-up runs, then timed runs, output discarded.
- **HTTP time** - from separate `--verbose` runs, the time spent inside HTTP requests. Requests sent at the same
  time count once.
- **CLI overhead** - end-to-end minus HTTP: startup, login, parsing and the CLI's own work.

You'll need the [harness's requirements](../tests/hands-on/README.md#requirements) and hyperfine. Then:

```bash
python3 tests/hands-on/bench.py --cli latest,local --umbraco 17   # the latest release against this checkout
python3 tests/hands-on/bench.py --cli local --note "a build ran alongside"   # say what was unusual
python3 tests/hands-on/bench.py --cli local --latency 25   # also time each command with +25 ms per request
```

Each run writes a results file to [`docs/performance/results/`](performance/results/). A release that was
packed but not pushed to NuGet installs from `./nupkg`. The
[harness README](../tests/hands-on/README.md#benchmarks-benchpy) has every option, the exact method and the
results file format.

## Updating the performance page

The performance page is generated from the results files. After recording a run, regenerate it and commit both:

```bash
python3 tests/hands-on/bench.py report
```

The page shows released versions only, each with its newest direct run. Builds of a checkout (`--cli local`) and
`--latency` runs stay in the results files.

## Why timings don't gate CI

- CI has no live Umbraco, so end-to-end timing can't run there, and one developer's machine can't gate anyone
  else's pull request.
- Timings on shared CI runners move between identical runs by more than the regressions worth catching.
- The likely regressions show up exactly anyway: extra requests in the request counts, extra work per item in the
  allocations.

## Reading the numbers

- **Noise.** Each number comes from one run on one machine. hyperfine's standard deviation (in the results file)
  is the spread to compare against: a difference smaller than the two spreads added together is noise. Other load
  on the machine widens the spread, so record runs you mean to commit on a quiet machine, and add a `--note` when
  that wasn't possible.
- **End-to-end includes Umbraco.** A real site, a remote host or another Umbraco version moves it. Compare CLI
  overhead across CLI versions, and HTTP time across Umbraco versions.
- **HTTP time is more than server time.** The first request of each run also opens the connection and warms up
  .NET's HTTP stack, so any command that makes a request has some HTTP time.
- **Versions do different work.** A later version can make more requests for a fuller result, so check the
  request counts before comparing times. A command a version can't run shows as `-`.
- **Older builds don't log every request.** Builds before 0.1.0-alpha.15 don't log the login request under
  `--verbose`, and builds before 0.1.0-alpha.14 log in on every run, so for them the login round trip lands in CLI
  overhead.
- **Machines differ.** Only compare numbers from the same machine.
