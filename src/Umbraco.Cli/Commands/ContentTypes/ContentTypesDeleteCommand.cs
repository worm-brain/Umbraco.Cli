using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>document-type delete</c> command.</summary>
public static class ContentTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>document-type delete</c> command: destructive, gated by confirmation, and
    /// refused before confirmation unless <c>--force</c>, because Umbraco deletes every document
    /// of the type with it and cannot say how many there are (#253).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a document type by id or alias.\n\n"
                + "Umbraco deletes every document of this type along with it, and cannot report how "
                + "many there are, so the delete is refused unless --force is given.\n\n"
                + "Example:\n  umbraco document-type delete blogPost --force --yes"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.DocumentType);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            SchemaKinds.DocumentType,
            idArg,
            "Delete the document type and every document of that type."
        );
        cmd.Destructive(parseResult =>
            $"Permanently delete document type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteDocumentTypeAsync(id, c),
                            c
                        ),
                    "Document type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
