using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content version rollback</c> command (issue #58).</summary>
public static class ContentRollbackCommand
{
    /// <summary>
    /// Builds the <c>content version rollback</c> command. The argument is a <em>version</em> id (from
    /// <c>content version list</c>), not a document id — rolling back restores the document to the
    /// state captured in that version. A rollback only changes the draft; <c>--publish</c> publishes
    /// the document afterwards (#233). Publishing turns the edited draft into the published
    /// version, so undoing a published edit means rolling back to the version before that one (#294).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "rollback",
            "Roll a content item back to a previous version.\n\nThe ID is a version ID from 'content version list', not the content ID. A rollback only changes the draft: the live site keeps the published version until you publish, or pass --publish.\n\nTo undo the last published edit, roll back to the version listed after the one marked isCurrentPublishedVersion: publishing turns the edited draft into the published version, so that version holds the edit.\n\nExamples:\n  umbraco content version rollback 7a1c2d3e-...\n  umbraco content version rollback 7a1c2d3e-... --culture en-US\n  umbraco content version rollback 7a1c2d3e-... --culture en-US --publish"
        ).Mutating();
        var versionIdArg = new Argument<Guid>("id")
        {
            Description = "Version ID to roll back to (see 'content version list').",
        };
        var cultureOpt = new Option<string?>("--culture")
        {
            Description = "ISO culture code to roll back (e.g. en-US).",
        };
        var publishOpt = new Option<bool>("--publish")
        {
            Description =
                "Publish the document after the rollback (the rolled-back culture, or every culture when --culture is omitted).",
        };
        cmd.Add(versionIdArg);
        cmd.Add(cultureOpt);
        cmd.Add(publishOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var versionId = parseResult.GetValue(versionIdArg);
                var culture = parseResult.GetValue(cultureOpt);
                var publish = parseResult.GetValue(publishOpt);
                return executor.RunMessageAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        // The positional is a version id; publishing needs its document. Read it
                        // before rolling back so a bad id fails without changing anything.
                        Guid? documentId = null;
                        if (publish)
                        {
                            var document = await client.GetVersionDocumentIdAsync(versionId, c);
                            if (!document.IsSuccess)
                                return UmbracoResponse<ItemRef>.FailureFrom(document);
                            documentId = document.Data;
                        }

                        var rollback = client.RollbackDocumentVersionAsync(versionId, culture, c);
                        return await (
                            documentId is { } id
                                ? rollback.ThenPublishAsync(
                                    client,
                                    id,
                                    culture is null ? null : [culture],
                                    "Rolled back",
                                    c
                                )
                                : rollback
                        ).Then(ItemRef.Of(versionId));
                    },
                    publish
                        ? "Content rolled back to the selected version and published."
                        : "Content rolled back to the selected version. Publish to make it live.",
                    ct
                );
            }
        );

        return cmd;
    }
}
