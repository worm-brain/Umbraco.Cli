using System.CommandLine;
using System.CommandLine.Parsing;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Reports a command-line parse error in the caller's chosen output format (#167).
/// <para>
/// System.CommandLine's own handling writes plain text to stderr and the full help screen to
/// stdout regardless of <c>--output</c>, so piping a mistyped command into <c>jq</c> failed on
/// the help text rather than reading an error envelope. "JSON output is always parseable" is the
/// contract the rest of the CLI keeps, and a typo is the most likely moment for a caller to need
/// it.
/// </para>
/// </summary>
public static class ParseErrorReporter
{
    /// <summary>
    /// Whether the parse result is really a help or version request. Those carry parse "errors"
    /// for the missing arguments they skipped, but the user asked for help and should get it.
    /// </summary>
    /// <param name="parsed">The parse result.</param>
    /// <returns>True when help or version was requested.</returns>
    public static bool IsHelpOrVersion(ParseResult parsed) =>
        parsed.Tokens.Any(t => t.Value is "--help" or "-h" or "-?" or "/?" or "--version" or "-v");

    /// <summary>
    /// Writes the parse errors as an error envelope and returns the exit code.
    /// </summary>
    /// <param name="parsed">The failed parse result.</param>
    /// <param name="globalOptions">The recursive global options, for <c>--output</c>.</param>
    /// <returns>Exit code 1, matching an invalid invocation.</returns>
    public static int Report(ParseResult parsed, GlobalOptions globalOptions)
    {
        // Read --output off the failed parse where possible. A malformed --output value is itself
        // a parse error, so fall back to the same redirect heuristic the writer factory uses.
        OutputFormat? requested = null;
        try
        {
            requested = OutputFormatParser.Parse(parsed.GetValue(globalOptions.Output));
        }
        catch (InvalidOperationException)
        {
            // Unparseable --output; the heuristic below decides.
        }

        var writer = OutputWriterFactory.Create(requested);
        var message = string.Join(" ", parsed.Errors.Select(e => e.Message));
        var command = parsed.CommandResult.Command.Name;

        writer.WriteError(
            1,
            $"{message} Run 'umbraco {command} --help' for usage.",
            commandName: command
        );
        return 1;
    }
}
