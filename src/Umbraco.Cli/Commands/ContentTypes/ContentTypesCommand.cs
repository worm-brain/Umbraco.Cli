using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesCommand
{
    public static Command Build(Option<string?> hostOpt, Option<string?> tokenOpt, Option<string?> outputOpt, CommandContextFactory factory)
    {
        var cmd = new Command("content-types", "Manage Umbraco document types.");
        cmd.Add(ContentTypesListCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentTypesGetCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentTypesCreateCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        cmd.Add(ContentTypesDeleteCommand.Build(hostOpt, tokenOpt, outputOpt, factory));
        return cmd;
    }
}
