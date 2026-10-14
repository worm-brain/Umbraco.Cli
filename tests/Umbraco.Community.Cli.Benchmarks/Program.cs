using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

// Every run writes the brief JSON report (<artifacts>/results/*-report-brief.json) next to
// BenchmarkDotNet's default Markdown/CSV/HTML ones: it is what the CI allocation gate compares
// (scripts/ci/benchmark-compare.ps1). Everything else - job, filter, artifacts folder - comes
// from the command line.
var summaries = BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, DefaultConfig.Instance.AddExporter(JsonExporter.Brief));

// BenchmarkDotNet records a benchmark that threw or failed to build in its summary without failing
// the process; a run that silently lost a benchmark would silently drop it from the gate, so fail.
return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;
