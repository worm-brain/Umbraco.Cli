using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Shared helpers for the diagnostics nouns (issue #115), which have several no-argument verbs that
/// simply render a single object from a client call (e.g. <c>server status</c>,
/// <c>models-builder dashboard</c>).
/// </summary>
internal static class DiagnosticsVerb
{
    /// <summary>Builds a no-argument verb that renders a single object from a client call.</summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="name">The verb name.</param>
    /// <param name="description">The verb help text.</param>
    /// <param name="call">The client call the verb delegates to.</param>
    /// <returns>The configured verb command.</returns>
    public static Command Object<T>(
        CommandExecutor executor,
        string name,
        string description,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call
    )
    {
        var cmd = new Command(name, description);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(parseResult, (client, c) => call(client, c), ct)
        );
        return cmd;
    }
}
