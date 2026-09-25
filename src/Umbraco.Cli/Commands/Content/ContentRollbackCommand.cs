using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content version rollback</c> command (issue #58).</summary>
public static class ContentRollbackCommand
{
    /// <summary>
    /// Builds the <c>content version rollback</c> command. The argument is a <em>version</em> id (from
    /// <c>content version list</c>), not a document id — rolling back restores the document to the
    /// state captured in that version.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "rollback",
            "Roll a content item back to a previous version.\n\nThe ID is a version ID from 'content version list', not the content ID.\n\nExamples:\n  umbraco content version rollback 7a1c2d3e-...\n  umbraco content version rollback 7a1c2d3e-... --culture en-US"
        ).Mutating();
        var versionIdArg = new Argument<Guid>("id")
        {
            Description = "Version ID to roll back to (see 'content version list').",
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
                    (client, c) =>
                        client
                            .RollbackDocumentVersionAsync(
                                parseResult.GetValue(versionIdArg),
                                parseResult.GetValue(cultureOpt),
                                c
                            )
                            .Then(ItemRef.Of(parseResult.GetValue(versionIdArg))),
                    "Content rolled back to the selected version.",
                    ct
                )
        );

        return cmd;
    }
}
