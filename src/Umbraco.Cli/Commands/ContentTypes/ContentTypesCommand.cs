using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("content-types", "List, inspect, and manage Umbraco document types (content types).\n\nExamples:\n  umbraco content-types list\n  umbraco content-types get textPage\n  umbraco content-types create --name \"Blog Post\" --alias blogPost");
        cmd.Add(ContentTypesListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentTypesGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentTypesCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentTypesDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
