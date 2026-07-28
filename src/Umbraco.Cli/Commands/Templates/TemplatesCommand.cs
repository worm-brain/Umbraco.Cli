using System.CommandLine;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "templates",
            "List, inspect, and manage Razor view templates defined in the Umbraco instance.\n\nExamples:\n  umbraco templates list\n  umbraco templates create --name Home --alias home"
        );
        cmd.Add(TemplatesListCommand.Build(executor));
        cmd.Add(TemplatesGetCommand.Build(executor));
        cmd.Add(TemplatesCreateCommand.Build(executor));
        cmd.Add(TemplatesUpdateCommand.Build(executor));
        cmd.Add(TemplatesDeleteCommand.Build(executor));
        return cmd;
    }
}
