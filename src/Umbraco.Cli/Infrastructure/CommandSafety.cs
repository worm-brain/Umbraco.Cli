using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The one place a command is declared destructive (#255).
/// <para>
/// A destructive command carries its confirmation prompt. <see cref="Commands.CommandExecutor"/>
/// reads it to gate the run behind <c>--yes</c>, and <see cref="CommandCatalog"/> reads it to report
/// <c>destructive: true</c> in <c>umbraco commands</c>. Before this, the gate was "did the call site
/// pass a prompt string" and the catalog was a list of verb names, and the two drifted apart.
/// </para>
/// </summary>
public static class CommandSafety
{
    /// <summary>One command's declaration.</summary>
    /// <param name="Prompt">Builds the confirmation text; null means this invocation is not destructive.</param>
    /// <param name="When">The option that makes the command destructive, or null when it always is.</param>
    private sealed record Declaration(Func<ParseResult, string?> Prompt, string? When);

    // Keyed by the Command instance, so the declaration lives and dies with the command tree
    // without adding a field to System.CommandLine's types.
    private static readonly ConditionalWeakTable<Command, Declaration> Declarations = new();

    /// <summary>
    /// Declares <paramref name="command"/> destructive: before it runs, the caller must confirm
    /// the prompt, or pass <c>--yes</c> when not interactive.
    /// </summary>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The leaf command.</param>
    /// <param name="prompt">Builds the confirmation text from the parsed command line.</param>
    /// <returns>The same command.</returns>
    public static TCommand Destructive<TCommand>(
        this TCommand command,
        Func<ParseResult, string> prompt
    )
        where TCommand : Command
    {
        Declarations.AddOrUpdate(command, new Declaration(prompt, When: null));
        return command;
    }

    /// <summary>
    /// Declares <paramref name="command"/> destructive only when <paramref name="flag"/> is set -
    /// e.g. <c>apply</c> with <c>--prune</c>. The condition and the option the catalog names both
    /// come from <paramref name="flag"/>, so they cannot disagree.
    /// </summary>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The leaf command.</param>
    /// <param name="flag">The option that makes the command destructive.</param>
    /// <param name="prompt">Builds the confirmation text when <paramref name="flag"/> is set.</param>
    /// <returns>The same command.</returns>
    public static TCommand DestructiveWith<TCommand>(
        this TCommand command,
        Option<bool> flag,
        Func<ParseResult, string> prompt
    )
        where TCommand : Command
    {
        Declarations.AddOrUpdate(
            command,
            new Declaration(p => p.GetValue(flag) ? prompt(p) : null, flag.Name)
        );
        return command;
    }

    /// <summary>Whether <paramref name="command"/> was declared destructive, always or conditionally.</summary>
    /// <param name="command">The command.</param>
    /// <returns>True when it carries a declaration.</returns>
    public static bool IsDeclared(Command command) => Declarations.TryGetValue(command, out _);

    /// <summary>Whether <paramref name="command"/> is destructive on every invocation.</summary>
    /// <param name="command">The command.</param>
    /// <returns>True when declared without a <c>when</c> option.</returns>
    public static bool IsAlwaysDestructive(Command command) =>
        Declarations.TryGetValue(command, out var d) && d.When is null;

    /// <summary>The option that makes <paramref name="command"/> destructive, if it is only conditionally so.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The option name, or null.</returns>
    public static string? DestructiveWhen(Command command) =>
        Declarations.TryGetValue(command, out var d) ? d.When : null;

    /// <summary>
    /// The confirmation prompt for the command that was parsed, or null when that command is not
    /// destructive (or this invocation of it is not).
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>The prompt text, or null.</returns>
    public static string? PromptFor(ParseResult parseResult) =>
        Declarations.TryGetValue(parseResult.CommandResult.Command, out var d)
            ? d.Prompt(parseResult)
            : null;
}
