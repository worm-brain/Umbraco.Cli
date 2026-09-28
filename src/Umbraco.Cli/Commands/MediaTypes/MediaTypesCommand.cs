using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-type</c> command group (issue #55).</summary>
public static class MediaTypesCommand
{
    /// <summary>Builds the <c>media-type</c> noun with its list/get/create/delete verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "media-type",
            "List, inspect, and manage Umbraco media types."
        ).WithExamples(
            "umbraco media-type list",
            "umbraco media-type get 3f7a8b2e-...",
            "umbraco media-type create --name \"Custom Image\" --alias customImage"
        );
        cmd.Add(MediaTypesListCommand.Build(executor));
        cmd.Add(MediaTypesGetCommand.Build(executor));
        cmd.Add(MediaTypesCreateCommand.Build(executor));
        cmd.Add(MediaTypesUpdateCommand.Build(executor));
        cmd.Add(MediaTypesDeleteCommand.Build(executor));
        return cmd;
    }
}
