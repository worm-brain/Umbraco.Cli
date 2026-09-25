using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using Umbraco.Cli.Client;
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
        var message = string.Join(" ", parsed.Errors.Select(e => Humanise(e.Message)));
        // The dotted name, matching meta.command everywhere else - "content.list", not "list".
        var path = new List<string>();
        for (
            var result = parsed.CommandResult;
            result is not null;
            result = result.Parent as CommandResult
        )
            path.Insert(0, result.Command.Name);
        var usage = string.Join(" ", path.Skip(1));
        var command = string.Join(".", path.Skip(1));

        // category lets a caller branch on "my command line was wrong" without parsing the
        // message (#203), the same way API failures carry request_rejected / server_error.
        writer.WriteError(
            1,
            $"{message} Run 'umbraco {usage} --help' for usage.".Replace("  ", " "),
            category: FailureCategory.InvalidArgument.ToWire(),
            commandName: string.IsNullOrEmpty(command) ? null : command
        );
        return 1;
    }

    // System.CommandLine's conversion error: "Cannot parse argument 'x' for option '--parent' as
    // expected type 'System.Nullable`1[System.Guid]'." (or "for command 'get'" for a positional).
    private static readonly System.Text.RegularExpressions.Regex ConversionError = new(
        @"Cannot parse argument '(?<value>.*?)' for (?<kind>option|command) '(?<name>.*?)' as expected type '(?:System\.Nullable`1\[)?(?<type>[\w.]+?)\]?'\.",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    /// <summary>
    /// The fallback for types <see cref="ValueParsing"/> does not cover (numbers with defaults,
    /// enums): rewrites System.CommandLine's type-conversion error into words a user can act on (#211):
    /// "'Blog' is not valid for --parent: expected a GUID id." rather than a .NET type name such as
    /// <c>System.Nullable`1[System.Guid]</c>. Any other message is returned as it is.
    /// </summary>
    /// <param name="message">A parse error message.</param>
    /// <returns>The message to show.</returns>
    internal static string Humanise(string message) =>
        ConversionError.Replace(
            message,
            m =>
                m.Groups["kind"].Value == "option"
                    ? $"'{m.Groups["value"].Value}' is not valid for {m.Groups["name"].Value}: expected {Expected(m.Groups["type"].Value)}."
                    // A positional argument: System.CommandLine names the command, not the argument.
                    : $"'{m.Groups["value"].Value}' is not a valid argument for {m.Groups["name"].Value}: expected {Expected(m.Groups["type"].Value)}."
        );

    /// <summary>How to describe a value of a .NET type to a user.</summary>
    private static string Expected(string type) =>
        type switch
        {
            "System.Guid" => "a GUID id",
            "System.Int32" or "System.Int64" => "a whole number",
            "System.Boolean" => "true or false",
            "System.DateTimeOffset" or "System.DateTime" =>
                "an ISO 8601 date and time, e.g. 2026-10-01T09:00:00Z",
            "System.Decimal" or "System.Double" => "a number",
            _ => $"a {type[(type.LastIndexOf('.') + 1)..]} value",
        };
}
