using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesCommand
{
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
