using System.CommandLine;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("templates", "List and inspect Razor view templates defined in the Umbraco instance.\n\nExample:\n  umbraco templates list");
        cmd.Add(TemplatesListCommand.Build(executor));
        cmd.Add(TemplatesGetCommand.Build(executor));
        return cmd;
    }
}
