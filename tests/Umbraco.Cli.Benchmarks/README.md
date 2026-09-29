# Umbraco.Cli.Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) benchmarks for the CPU and memory cost of the code
the CLI owns: startup wiring, output writers, response parsing and the export/diff/apply engines.
Everything runs on in-memory fixtures, so no Umbraco instance is needed. End-to-end timing against
a real site is the hyperfine harness's job (`tests/hands-on`), not this project's.

This is not a test project: `dotnet test` skips it and it is never packed into the tool.

## Run it locally

```bash
# Every benchmark, with BenchmarkDotNet's default (accurate, slower) job. Takes several minutes.
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*"

# The same short job CI uses (about 2-3 minutes on a fast machine).
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*" --job short

# One class, or one benchmark.
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*OutputBenchmarks*"
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*SchemaPipelineBenchmarks.Diff"

# List the benchmarks without running them.
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --list flat
```

`-c Release` is required: BenchmarkDotNet refuses to measure a debug build. Any other
BenchmarkDotNet command-line option works after `--` (`--job dry` for a one-iteration smoke run,
`--artifacts <dir>` to choose where results go).

### Results

Each run writes, per benchmark class, to `BenchmarkDotNet.Artifacts/results/` (or
`<artifacts>/results/`):

| File | Contents |
|---|---|
| `Umbraco.Cli.Benchmarks.<Class>-report-brief.json` | Machine-readable results; what the CI gate reads |
| `Umbraco.Cli.Benchmarks.<Class>-report-github.md` | The summary table as Markdown |
| `Umbraco.Cli.Benchmarks.<Class>-report.csv` / `.html` | The same table as CSV and HTML |

In the JSON, `Benchmarks[]` has one entry per benchmark: `FullName` (e.g.
`Umbraco.Cli.Benchmarks.OutputBenchmarks.Json`) identifies it across runs, `Statistics.Mean` is
the mean time in nanoseconds, and `Memory.BytesAllocatedPerOperation` is the allocated bytes per
operation. `HostEnvironmentInfo` records the machine, OS and runtime.

The process exits non-zero when any benchmark failed, so a broken benchmark cannot drop out of the
gate unnoticed.

## What each benchmark measures

Every class has `[MemoryDiagnoser]`, so each benchmark reports allocated bytes per operation as
well as time. Fixtures are built by `Fixtures.cs` from their index alone (no clock, no random), so
every run measures identical input.

| Class | Benchmark | Measures |
|---|---|---|
| `StartupBenchmarks` | `BuildContainer` | Registering the CLI's services and resolving the command executor (`CliServices.CreateProvider`) |
| | `BuildContainerAndCommandTree` | The above plus building the whole command tree, as `Program.cs` does on every run |
| | `Parse` | Parsing `content list --parent <id> --take 50 --output json` against a built tree |
| `OutputBenchmarks` | `Json` | `-o json` for a 2,000-row `content list` result |
| | `JsonWithFields` | `-o json --fields id,name` for the same rows (the DOM-projection path) |
| | `Csv` | `-o csv` for the same rows |
| | `HumanTable` | The Spectre.Console table a terminal shows, on a fixed 160-column console |
| `KiotaParsingBenchmarks` | `ParseDocumentTreePage` | Kiota parsing a 1,000-item `tree/document/children` page into the generated model |
| | `ParseLargeDocument` | Kiota parsing a `document/{id}` body with 508 property values (untyped nodes) |
| | `ClientGetDocumentRaw` | `UmbracoManagementClient.GetDocumentRawAsync` end to end over a stub HTTP handler: request adapter, error mapping and DOM parse of the same document |
| `ContentPipelineBenchmarks` | `Diff` | `content diff` classifying 2,000 documents (added, removed, changed values, publish-state changes) |
| | `WriteSnapshot` | `content export` serialising the 2,000-document snapshot |
| | `ReadSnapshot` | `content diff`/`apply` reading and validating that snapshot file |
| `SchemaPipelineBenchmarks` | `Diff` | `schema diff` over 150 document types (20 properties each), 60 data types, 40 templates, 3 languages and 300 dictionary items |
| | `WriteSnapshot` | `schema export` serialising that snapshot |
| | `ReadSnapshot` | `schema diff`/`apply` reading and validating that snapshot file |

Stdout is redirected to `TextWriter.Null` in the output benchmarks, so they measure serialising and
rendering, not the terminal. Apply's writes need a server and are out of scope: its plan is the
diff.

## The CI gate

`.github/workflows/benchmarks.yml` runs the suite:

- on pull requests labelled `performance` (adding the label starts a run),
- weekly on `main` (Mondays), and
- on demand (Actions, Benchmarks, Run workflow).

On a pull request it checks out the PR and `main` on the same runner, runs both with `--job short`,
and compares them with [`scripts/ci/benchmark-compare.ps1`](../../scripts/ci/benchmark-compare.ps1):

- **Allocations gate the build.** The job fails when a benchmark's allocated bytes per operation
  rise more than **10%** over `main`. Allocations are deterministic for a given input, so this
  catches real regressions (an extra copy per item, a lost buffer reuse) without flaking.
- **Timings are reported, never gated.** Shared runners are too noisy for a timing threshold.
- **New and removed benchmarks** are listed but not gated. A benchmark that stops reporting
  allocations fails, since that would switch the gate off for it.
- **No baseline** (a `main` without this project, before it first merged) skips the comparison
  with a notice rather than failing.

The comparison table is written to the job summary. The JSON reports for both runs are uploaded as
the `benchmark-results` artifact (kept 90 days), which is the results history; nothing is pushed to
the repository.

`--job short` is BenchmarkDotNet's ShortRun (1 launch, 3 warm-up and 3 measured iterations). It
keeps two full runs within a few minutes of CI time. Its timings are rougher than the default
job's, which is acceptable because they are only reported; allocations are measured the same way
under any job.

### Run the comparison locally

```bash
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*" --job short --artifacts ./bench/before
# ...make the change...
dotnet run -c Release --project tests/Umbraco.Cli.Benchmarks -- --filter "*" --job short --artifacts ./bench/after
pwsh ./scripts/ci/benchmark-compare.ps1 -Baseline ./bench/before -Candidate ./bench/after -BaselineLabel before -CandidateLabel after
```

### When the gate fails

Look at what the benchmark exercises and find the new allocation (a profiler, or
`--profiler ETW`/`EP` on the benchmark). If the rise is intended, say why in the pull request;
once it merges, `main` carries the new figure and later PRs compare against that.

## Adding a benchmark

- Put it in the class for its area (or a new `*Benchmarks` class) and give the class
  `[MemoryDiagnoser]`.
- Build its input in `Fixtures.cs` from indexes only, and in `[GlobalSetup]` rather than in the
  benchmark, so only the code under test is measured.
- Do not touch the network, the filesystem or the clock inside a benchmark.
- Renaming a benchmark changes its `FullName`, so the gate treats it as new for one PR.
- Add it to the table above.
