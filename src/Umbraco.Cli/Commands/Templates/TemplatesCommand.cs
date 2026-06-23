using System.CommandLine;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("templates", "List and inspect Razor view templates defined in the Umbraco instance.\n\nExample:\n  umbraco templates list");
        cmd.Add(TemplatesListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(TemplatesGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
