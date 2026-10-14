using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Templates;

/// <summary>Wires the <c>template</c> noun: Razor view templates.</summary>
public static class TemplatesCommand
{
    /// <summary>Builds the <c>template</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "template",
            "List, inspect, and manage Razor view templates defined in the Umbraco instance."
        ).WithExamples("umbraco template list", "umbraco template create --name Home --alias home");
        cmd.Add(TemplatesListCommand.Build(executor));
        cmd.Add(TemplatesGetCommand.Build(executor));
        cmd.Add(TemplatesCreateCommand.Build(executor));
        cmd.Add(TemplatesUpdateCommand.Build(executor));
        cmd.Add(TemplatesDeleteCommand.Build(executor));
        return cmd;
    }
}
