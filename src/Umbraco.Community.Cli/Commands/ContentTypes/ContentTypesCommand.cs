using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>document-type</c> noun: the types documents are created from.</summary>
public static class ContentTypesCommand
{
    /// <summary>Builds the <c>document-type</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "document-type",
            "List, inspect, and manage Umbraco document types."
        ).WithExamples(
            "umbraco document-type list",
            "umbraco document-type get textPage",
            "umbraco document-type create --name \"Blog Post\" --alias blogPost"
        );
        cmd.Add(ContentTypesListCommand.Build(executor));
        cmd.Add(ContentTypesGetCommand.Build(executor));
        cmd.Add(ContentTypesCreateCommand.Build(executor));
        cmd.Add(ContentTypesUpdateCommand.Build(executor));
        cmd.Add(ContentTypesDeleteCommand.Build(executor));
        return cmd;
    }
}
