using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

public static class ContentTypesDeleteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a document type by UUID. All content of this type must be removed first.\n\nExample:\n  umbraco content-types delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete document type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "content-types.delete",
                    (client, c) => client.DeleteDocumentTypeAsync(parseResult.GetValue(idArg), c),
                    "Document type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
