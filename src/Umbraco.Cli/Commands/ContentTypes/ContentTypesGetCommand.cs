using System.CommandLine;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a document type by UUID.\n\nReturns the type's core fields only: id, name, alias, description, isElement, allowedAsRoot. Properties, groups, templates, allowed children and compositions are NOT returned - use 'umbraco schema export' to read the full definition.\n\nAn alias is not accepted here yet; resolve it with 'umbraco content-types list' first.\n\nExample:\n  umbraco content-types get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content-types.get",
                    (client, c) => client.GetDocumentTypeByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
