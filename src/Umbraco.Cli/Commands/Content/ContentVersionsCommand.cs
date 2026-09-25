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
            "List the version history of a content item. A culture-variant item is listed across all "
                + "its cultures, each row tagged with its culture, unless --culture is given.\n\nExamples:\n  umbraco content versions 3f7a8b2e-...\n  umbraco content versions 3f7a8b2e-... --culture en-US"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        var cultureOpt = new Option<string?>("--culture")
        {
            Description =
                "ISO culture code to list versions for (e.g. en-US). Lists every culture if omitted.",
        };
        cmd.Add(idArg);
        cmd.Add(cultureOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "content.versions",
                    (client, skip, take, c) =>
                        client.GetDocumentVersionsAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(cultureOpt),
                            skip,
                            take,
                            c
                        ),
                    ["Version ID", "Culture", "Date", "Draft", "Published"],
                    v =>
                        new[]
                        {
                            v.Id.ToString(),
                            v.Culture ?? "",
                            v.VersionDate.ToString("u"),
                            v.IsCurrentDraftVersion ? "yes" : "",
                            v.IsCurrentPublishedVersion ? "yes" : "",
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
