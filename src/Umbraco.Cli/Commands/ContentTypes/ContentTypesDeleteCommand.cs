using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>document-type delete</c> command.</summary>
public static class ContentTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>document-type delete</c> command: destructive, gated by confirmation, and
    /// refused before confirmation unless <c>--force</c> while anything uses the type (#253, #287):
    /// Umbraco deletes every document of the type with it, so the documents are counted (the
    /// recycle bin included), and a type used as a composition or an element type is refused too.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a document type by id or alias.\n\n"
                + "Umbraco deletes every document of this type along with it. The delete is refused "
                + "unless --force is given while any document uses the type (the recycle bin "
                + "included), another type uses it as a composition, or it is an element type."
        )
            .WithExamples(
                "umbraco document-type delete blogPost --yes",
                "umbraco document-type delete blogPost --force --yes"
            )
            .Mutating();
        var idArg = Reference.Argument(EntityKind.DocumentType);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            idArg,
            "Delete the document type even while documents or other types use it."
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
                            id => client.DeleteDocumentTypeAsync(id, c).Then(ItemRef.Of(id)),
                            c
                        ),
                    "Document type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
