using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Http;
using Umbraco.Cli.Infrastructure.Output;

// Write UTF-8 everywhere, so non-ASCII text (the "Søg" example in dictionary help, Danish or
// Japanese names in JSON) survives on the Windows console and through pipes alike.
ConsoleEncoding.UseUtf8();

// Honour NO_COLOR (#94): strip colour from all Spectre.Console output when the variable is present.
ConsoleColorSetup.ApplyFromEnvironment();

// ── DI and root command ───────────────────────────────────────────────────────
// Both are assembled in CliServices, so the startup benchmarks (#409) build exactly what runs here;
// the tree itself comes from CliRoot, which the tests and the surface snapshot also use.
var sp = CliServices.CreateProvider();
var globalOptions = sp.GetRequiredService<GlobalOptions>();
var root = CliServices.BuildRoot(sp);

// ── Run ───────────────────────────────────────────────────────────────────────
// Parse with response-file expansion disabled (#115) so option values beginning with '@'
// (e.g. Serilog log-viewer filters like "@Level='Error'") are passed through verbatim.
var parsed = root.Parse(args, CliParserConfiguration.Create());

// Switch on the HTTP logging for every client at once, before any command (auth login and
// auth doctor included) makes a request (#374).
sp.GetRequiredService<VerboseState>().Enabled = parsed.GetValue(globalOptions.Verbose);

// #167: System.CommandLine reports a parse error as plain text plus the help screen, whichever
// output format was asked for - so `... -o json | jq` failed on the help text instead of reading
// an error envelope. Emit the same envelope every other failure uses before handing over.
if (parsed.Errors.Count > 0 && !ParseErrorReporter.IsHelpOrVersion(parsed))
    return ParseErrorReporter.Report(parsed, globalOptions);

return await parsed.InvokeAsync();
