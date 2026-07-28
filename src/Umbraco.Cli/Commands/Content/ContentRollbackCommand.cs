using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content rollback</c> command (issue #58).</summary>
public static class ContentRollbackCommand
{
    /// <summary>
    /// Builds the <c>content rollback</c> command. The argument is a <em>version</em> id (from
    /// <c>content versions</c>), not a document id — rolling back restores the document to the
    /// state captured in that version.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "rollback",
            "Roll a content item back to a previous version.\n\nThe ID is a version ID from 'content versions', not the content ID.\n\nExamples:\n  umbraco content rollback 7a1c2d3e-...\n  umbraco content rollback 7a1c2d3e-... --culture en-US"
        );
        var versionIdArg = new Argument<Guid>("version-id")
        {
            Description = "Version ID to roll back to (see 'content versions').",
        };
        var cultureOpt = new Option<string?>("--culture")
        {
            Description = "ISO culture code to roll back (e.g. en-US).",
        };
        cmd.Add(versionIdArg);
        cmd.Add(cultureOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "content.rollback",
                    (client, c) =>
                        client.RollbackDocumentVersionAsync(
                            parseResult.GetValue(versionIdArg),
                            parseResult.GetValue(cultureOpt),
                            c
                        ),
                    "Content rolled back to the selected version.",
                    ct
                )
        );

        return cmd;
    }
}
