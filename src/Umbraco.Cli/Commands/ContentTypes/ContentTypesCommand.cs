using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "content-types",
            "List, inspect, and manage Umbraco document types (content types).\n\nExamples:\n  umbraco content-types list\n  umbraco content-types get textPage\n  umbraco content-types create --name \"Blog Post\" --alias blogPost"
        );
        cmd.Add(ContentTypesListCommand.Build(executor));
        cmd.Add(ContentTypesGetCommand.Build(executor));
        cmd.Add(ContentTypesCreateCommand.Build(executor));
        cmd.Add(ContentTypesDeleteCommand.Build(executor));
        return cmd;
    }
}
