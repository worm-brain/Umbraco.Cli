using System.CommandLine;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The dotted name of the command that was parsed (<c>content.domain.set</c>), derived from the
/// command tree. It is the one source of <c>meta.command</c>, the allow-list's command names and
/// the parse-error usage hint, so none of them can drift from the real command path when a
/// command is renamed.
/// </summary>
public static class CommandPath
{
    /// <summary>The command names from the first noun down to the parsed command.</summary>
    /// <param name="parsed">The parse result.</param>
    /// <returns>The segments, empty when only the root was parsed.</returns>
    public static IReadOnlyList<string> Segments(ParseResult parsed)
    {
        var path = new List<string>();
        for (
            var result = parsed.CommandResult;
            result is not null;
            result = result.Parent as System.CommandLine.Parsing.CommandResult
        )
            path.Insert(0, result.Command.Name);
        // The first segment is the root command, whose name is the executable's - not part of the path.
        return path.Skip(1).ToList();
    }

    /// <summary>The dotted command name, e.g. <c>content.list</c>.</summary>
    /// <param name="parsed">The parse result.</param>
    /// <returns>The dotted name, or null when only the root was parsed.</returns>
    public static string? Of(ParseResult parsed)
    {
        var segments = Segments(parsed);
        return segments.Count == 0 ? null : string.Join(".", segments);
    }
}
