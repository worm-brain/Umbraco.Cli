using System.CommandLine;
using System.Runtime.CompilerServices;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The one place a command declares a field of its own in its success envelope's <c>meta</c>
/// (#440), such as <c>dictionary get</c>'s <c>valueFormat</c>. Like <see cref="CommandSafety"/>, the
/// declaration lives beside the command rather than in its action, so every <c>Run*</c> helper of
/// <see cref="Commands.CommandExecutor"/> honours it without a new overload.
/// <para>
/// The executor reads the fields after the command's call succeeds and before it renders, so a
/// failed call, an aborted run or a <c>--dry-run</c> preview never pays for them.
/// </para>
/// </summary>
public static class CommandMeta
{
    /// <summary>One declared field.</summary>
    /// <param name="Key">The meta field name, camelCase.</param>
    /// <param name="Read">Reads the value; null leaves the field out.</param>
    private sealed record Field(
        string Key,
        Func<IUmbracoManagementClient, CancellationToken, Task<object?>> Read
    );

    // Keyed by the Command instance, as CommandSafety's declarations are.
    private static readonly ConditionalWeakTable<Command, List<Field>> Fields = new();

    /// <summary>Declares a field <paramref name="command"/> adds to its success envelope's <c>meta</c>.</summary>
    /// <typeparam name="TCommand">The command type, returned for chaining.</typeparam>
    /// <param name="command">The leaf command.</param>
    /// <param name="key">The meta field name, camelCase. It must not be a standard meta field.</param>
    /// <param name="read">Reads the value from the site; null leaves the field out.</param>
    /// <returns>The same command.</returns>
    public static TCommand WithMeta<TCommand>(
        this TCommand command,
        string key,
        Func<IUmbracoManagementClient, CancellationToken, Task<object?>> read
    )
        where TCommand : Command
    {
        Fields.GetOrCreateValue(command).Add(new Field(key, read));
        return command;
    }

    /// <summary>
    /// Reads the fields the parsed command declares and adds them to <paramref name="output"/>.
    /// Does nothing (and makes no request) for a command that declares none.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="client">The authenticated client.</param>
    /// <param name="output">The writer the command renders with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when every field has been added.</returns>
    public static async Task AddToAsync(
        ParseResult parseResult,
        IUmbracoManagementClient client,
        IOutputWriter output,
        CancellationToken ct
    )
    {
        if (!Fields.TryGetValue(parseResult.CommandResult.Command, out var fields))
            return;
        foreach (var field in fields)
            output.AddMeta(field.Key, await field.Read(client, ct));
    }
}
