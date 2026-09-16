using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content versions</c> command (issue #58).</summary>
public static class ContentVersionsCommand
{
    /// <summary>Builds the <c>content versions</c> command (list a document's version history).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "versions",
            "List the version history of a content item.\n\nExamples:\n  umbraco content versions 3f7a8b2e-...\n  umbraco content versions 3f7a8b2e-... --culture en-US"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var cultureOpt = new Option<string?>("--culture")
        {
            Description = "ISO culture code to filter versions by (e.g. en-US).",
        };
        cmd.Add(idArg);
        cmd.Add(cultureOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "content.versions",
                    (client, c) =>
                        client.GetDocumentVersionsAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(cultureOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["Version ID", "Date", "Draft", "Published"],
                    data =>
                        data?.Items.Select(v =>
                            new[]
                            {
                                v.Id.ToString(),
                                v.VersionDate.ToString("u"),
                                v.IsCurrentDraftVersion ? "yes" : "",
                                v.IsCurrentPublishedVersion ? "yes" : "",
                            }
                        )
                        ?? [],
                    ct
                )
        );

        return cmd;
    }
}
