using System.CommandLine;
using System.Runtime.CompilerServices;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The one place a command is declared destructive (#255) or, when its verb does not say so, a
/// write.
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

    // Commands declared as writes whose verb the catalog's verb set cannot classify (a `sort`
    // is a PUT, `health run` a POST). The value is unused; only membership matters.
    private static readonly ConditionalWeakTable<Command, object> MutatingDeclarations = new();

    /// <summary>
    /// Declares <paramref name="command"/> a server write, so <c>umbraco commands</c> reports it
    /// <c>mutating: true</c> whatever its verb. Use it on any leaf that sends a non-GET request
    /// under a verb outside the catalog's standard write verbs. Destructive commands need not
    /// call it: a destructive command is always mutating.
    /// </summary>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The leaf command.</param>
    /// <returns>The same command.</returns>
    public static TCommand Mutating<TCommand>(this TCommand command)
        where TCommand : Command
    {
        MutatingDeclarations.AddOrUpdate(command, true);
        return command;
    }

    /// <summary>
    /// Whether <paramref name="command"/> was declared a write, either explicitly via
    /// <see cref="Mutating{TCommand}"/> or implicitly by being declared destructive.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>True when it carries either declaration.</returns>
    public static bool IsDeclaredMutating(Command command) =>
        MutatingDeclarations.TryGetValue(command, out _) || IsDeclared(command);

    // Writes whose result is report data rather than a write confirmation (#393): `health run` is a
    // POST, but what it returns is the diagnostic it was run for. The value is unused.
    private static readonly ConditionalWeakTable<Command, object> ReportDeclarations = new();

    /// <summary>
    /// Declares that <paramref name="command"/>'s success result is report data the caller ran it
    /// for, so <c>--quiet</c> still prints it (#393). This only affects <c>--quiet</c>: the command
    /// stays whatever <see cref="Mutating{TCommand}"/> made it, so <c>--readonly</c> still blocks it,
    /// <c>--dry-run</c> still previews it, and <c>umbraco commands</c> still reports it
    /// <c>mutating: true</c>.
    /// </summary>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The leaf command.</param>
    /// <returns>The same command.</returns>
    public static TCommand ReportsResult<TCommand>(this TCommand command)
        where TCommand : Command
    {
        ReportDeclarations.AddOrUpdate(command, true);
        return command;
    }

    /// <summary>
    /// Whether <c>--quiet</c> drops <paramref name="command"/>'s success result: true for a
    /// declared write (<see cref="IsDeclaredMutating"/>) unless it was declared with
    /// <see cref="ReportsResult{TCommand}"/>.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>True when its result is a write result that <c>--quiet</c> suppresses.</returns>
    public static bool QuietDropsResult(Command command) =>
        IsDeclaredMutating(command) && !ReportDeclarations.TryGetValue(command, out _);

    // Pre-flight checks that can refuse a run before it is confirmed (#246, #253). Kept apart
    // from the prompts: a check reads the server, a prompt does not.
    private static readonly ConditionalWeakTable<
        Command,
        Func<ParseResult, Client.IUmbracoManagementClient, CancellationToken, Task<string?>>
    > Refusals = new();

    /// <summary>
    /// Registers a check the executor runs after connecting and <b>before</b> the confirmation
    /// prompt. A non-null result refuses the run (exit 2) with that message, so a caller is never
    /// asked to confirm something that would then be refused.
    /// </summary>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The leaf command.</param>
    /// <param name="check">Returns why the run is refused, or null to let it proceed.</param>
    /// <returns>The same command.</returns>
    public static TCommand RefuseWhen<TCommand>(
        this TCommand command,
        Func<ParseResult, Client.IUmbracoManagementClient, CancellationToken, Task<string?>> check
    )
        where TCommand : Command
    {
        Refusals.AddOrUpdate(command, check);
        return command;
    }

    /// <summary>Runs the parsed command's pre-flight check, if it has one.</summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="client">The client the check reads with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Why the run is refused, or null.</returns>
    public static Task<string?> RefusalFor(
        ParseResult parseResult,
        Client.IUmbracoManagementClient client,
        CancellationToken ct
    ) =>
        Refusals.TryGetValue(parseResult.CommandResult.Command, out var check)
            ? check(parseResult, client, ct)
            : Task.FromResult<string?>(null);

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
