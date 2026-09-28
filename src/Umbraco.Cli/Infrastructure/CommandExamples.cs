using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Holds each command's help examples as data (#276). A command declares its example command lines
/// once, with <see cref="WithExamples{TCommand}"/>; they are rendered into the <c>--help</c>
/// description as the <c>Examples:</c> block and kept as a list, so the help-text tests and
/// <c>umbraco commands</c> read the lines directly instead of searching the description for the
/// heading and cutting the block back out.
/// </summary>
public static class CommandExamples
{
    /// <summary>The heading line that starts the rendered block in a description.</summary>
    public const string Heading = "Examples:";

    /// <summary>The indent each example line gets under <see cref="Heading"/>.</summary>
    public const string Indent = "  ";

    // Keyed by the Command instance, so the list lives and dies with the command tree without
    // adding a field to System.CommandLine's types (the same approach as CommandSafety).
    private static readonly ConditionalWeakTable<Command, IReadOnlyList<string>> Declarations =
        new();

    /// <summary>
    /// Declares <paramref name="command"/>'s help examples and appends them to its description as
    /// a blank line, the <c>Examples:</c> heading and one indented line per example - the layout
    /// every command's help has always used, so <c>--help</c> reads the same as before.
    /// </summary>
    /// <remarks>
    /// Each example is a real command line (it is parsed by <c>HelpTextTests</c>), using only the
    /// placeholders in <see cref="ExamplePlaceholders"/>. Shell around it - a trailing
    /// <c># comment</c>, a pipe, a <c>&gt; file</c> redirect - stays part of the text.
    /// Call it once per command, straight after construction: the block goes at the end of the
    /// description as it stands, and a second call is refused rather than rendering a second block.
    /// </remarks>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The command the examples belong to.</param>
    /// <param name="examples">The example command lines, without indentation.</param>
    /// <returns>The same command.</returns>
    /// <exception cref="ArgumentException">No examples were given, or one is blank or spans lines.</exception>
    /// <exception cref="InvalidOperationException">The command already declared its examples.</exception>
    public static TCommand WithExamples<TCommand>(this TCommand command, params string[] examples)
        where TCommand : Command
    {
        // A block with no lines, or a line the renderer would split, would not read back as the
        // list that was declared - refuse it at build time rather than render something odd.
        if (examples.Length == 0)
            throw new ArgumentException("Give at least one example.", nameof(examples));
        if (examples.Any(e => string.IsNullOrWhiteSpace(e) || e.Contains('\n')))
            throw new ArgumentException(
                "Each example must be one non-blank line.",
                nameof(examples)
            );
        if (Declarations.TryGetValue(command, out _))
            throw new InvalidOperationException(
                $"Command '{command.Name}' already declared its examples."
            );

        Declarations.Add(command, [.. examples]);
        command.Description = (command.Description ?? "") + Render(examples);
        return command;
    }

    /// <summary>The examples <paramref name="command"/> declared, in order.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The example lines, or an empty list when it declared none.</returns>
    public static IReadOnlyList<string> Of(Command command) =>
        Declarations.TryGetValue(command, out var examples) ? examples : [];

    /// <summary>The text <see cref="WithExamples{TCommand}"/> appends to a description.</summary>
    /// <param name="examples">The example lines.</param>
    /// <returns>A blank line, the heading, and each example indented on its own line.</returns>
    private static string Render(IEnumerable<string> examples) =>
        "\n\n" + Heading + "\n" + string.Join("\n", examples.Select(e => Indent + e));
}
