using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Auth;

public static class WhoAmICommand
{
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
