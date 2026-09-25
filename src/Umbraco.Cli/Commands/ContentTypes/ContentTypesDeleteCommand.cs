using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>content-types delete</c> command.</summary>
public static class ContentTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>content-types delete</c> command: destructive, gated by confirmation, and
    /// refused without <c>--force</c> because Umbraco deletes every document of the type with it
    /// and cannot say how many there are (#253).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a document type by UUID.\n\n"
                + "Umbraco deletes every document of this type along with it, and cannot report how "
                + "many there are, so the delete is refused unless --force is given.\n\n"
                + "Example:\n  umbraco content-types delete 3f7a8b2e-... --force --yes"
        );
        var idArg = new Argument<Guid>("id");
        var forceOpt = new Option<bool>(InUseGuard.ForceOption)
        {
            Description = "Delete the document type and every document of that type.",
        };
        cmd.Add(idArg);
        cmd.Add(forceOpt);
        cmd.Destructive(parseResult =>
            $"Permanently delete document type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "content-types.delete",
                    (client, c) =>
                    {
                        var id = parseResult.GetValue(idArg);
                        InUseGuard.Refuse(
                            InUseGuard.DocumentType(id),
                            parseResult.GetValue(forceOpt)
                        );
                        return client.DeleteDocumentTypeAsync(id, c);
                    },
                    "Document type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
