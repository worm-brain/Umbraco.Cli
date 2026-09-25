using System.CommandLine;
using System.CommandLine.Parsing;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Gives every single-value id and date option and argument in the command tree a parser that
/// writes a readable error (#211, #250 Phase 3 review). System.CommandLine's own conversion error
/// names the .NET type - <c>Cannot parse argument 'Blog' ... as expected type
/// 'System.Nullable`1[System.Guid]'</c> - and rewriting that text afterwards
/// (<see cref="ParseErrorReporter.Humanise"/>) depends on the library's English wording. Applied
/// once to the finished tree, so no command has to remember it; an option or argument that
/// already has its own parser is left alone.
/// </summary>
public static class ValueParsing
{
    /// <summary>Walks <paramref name="command"/> and its subcommands, installing the parsers.</summary>
    /// <param name="command">The root (or any) command.</param>
    public static void Apply(Command command)
    {
        foreach (var option in command.Options)
            Install(option);
        foreach (var argument in command.Arguments)
            Install(argument);
        foreach (var sub in command.Subcommands)
            Apply(sub);
    }

    private static void Install(Symbol symbol)
    {
        switch (symbol)
        {
            case Option<Guid> o when o.CustomParser is null:
                o.CustomParser = r => Parse<Guid>(r, OptionLabel(o), Guid.TryParse, "a GUID id");
                break;
            case Option<Guid?> o when o.CustomParser is null:
                o.CustomParser = r =>
                    ParseOptional<Guid>(r, OptionLabel(o), Guid.TryParse, "a GUID id");
                break;
            case Argument<Guid> a when a.CustomParser is null:
                a.CustomParser = r => Parse<Guid>(r, ArgumentLabel(a), Guid.TryParse, "a GUID id");
                break;
            case Argument<Guid?> a when a.CustomParser is null:
                a.CustomParser = r =>
                    ParseOptional<Guid>(r, ArgumentLabel(a), Guid.TryParse, "a GUID id");
                break;
            case Option<DateTimeOffset?> o when o.CustomParser is null:
                o.CustomParser = r =>
                    ParseOptional<DateTimeOffset>(
                        r,
                        OptionLabel(o),
                        (string s, out DateTimeOffset v) =>
                            DateTimeOffset.TryParse(
                                s,
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind,
                                out v
                            ),
                        "an ISO 8601 date and time, e.g. 2026-10-01T09:00:00Z"
                    );
                break;
        }
    }

    private static string OptionLabel(Option option) => $"is not valid for {option.Name}";

    private static string ArgumentLabel(Argument argument) => $"is not a valid {argument.Name}";

    private static T Parse<T>(
        ArgumentResult result,
        string label,
        ListOption.ItemParser<T> parse,
        string expected
    )
        where T : struct
    {
        var text = result.Tokens.Count > 0 ? result.Tokens[0].Value : "";
        if (parse(text, out var value))
            return value;
        // Ends with a full stop: the parse-error reporter appends "Run '... --help' for usage."
        result.AddError($"'{text}' {label}: expected {expected}.");
        return default;
    }

    private static T? ParseOptional<T>(
        ArgumentResult result,
        string label,
        ListOption.ItemParser<T> parse,
        string expected
    )
        where T : struct => result.Tokens.Count == 0 ? null : Parse(result, label, parse, expected);
}
