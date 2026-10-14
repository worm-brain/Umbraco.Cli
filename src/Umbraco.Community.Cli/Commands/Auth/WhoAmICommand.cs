using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Auth;

/// <summary>Wires the <c>auth whoami</c> command.</summary>
public static class WhoAmICommand
{
    /// <summary>Builds the <c>auth whoami</c> command: who the credentials sign in as, on which instance.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "whoami",
            "Show the currently authenticated backoffice user and instance details."
        ).WithExamples("umbraco auth whoami", "umbraco auth whoami --output json");

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetCurrentUserAsync(c),
                    ct
                )
        );

        return cmd;
    }
}
