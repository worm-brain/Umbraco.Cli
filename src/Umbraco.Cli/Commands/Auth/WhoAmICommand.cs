using System.CommandLine;

namespace Umbraco.Cli.Commands.Auth;

public static class WhoAmICommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("whoami", "Show the currently authenticated backoffice user and instance details.\n\nExamples:\n  umbraco auth whoami\n  umbraco auth whoami --output json");

        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "auth.whoami",
            (client, c) => client.GetCurrentUserAsync(c),
            ct));

        return cmd;
    }
}
