using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a document type by UUID, including its properties, property groups, allowed templates and compositions.\n\nAn alias is not accepted here yet (#159); resolve it with 'umbraco content-types list' first.\n\nExample:\n  umbraco content-types get 3f7a8b2e-..."
        );
        var idArg = new Argument<string>("id")
        {
            Description = "Document type alias (e.g. blogPost) or UUID.",
        };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content-types.get",
                    (client, c) => client.GetDocumentTypeAsync(parseResult.GetValue(idArg)!, c),
                    ct
                )
        );

        return cmd;
    }
}
