using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content copy</c> command (issue #67).</summary>
public static class ContentCopyCommand
{
    /// <summary>Builds the <c>content copy</c> command (copy an item under a new parent).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "copy",
            "Copy a content item under a new parent.\n\nExamples:\n  umbraco content copy 3f7a8b2e-... --parent 1a2b3c4d-...\n  umbraco content copy 3f7a8b2e-... --include-descendants"
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Content item ID to copy." };
        var parentOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Target parent ID. Copies to the content root if omitted.",
        };
        var descendantsOpt = new Option<bool>("--include-descendants")
        {
            DefaultValueFactory = _ => false,
            Description = "Also copy the item's descendants.",
        };
        var relateOpt = new Option<bool>("--relate")
        {
            DefaultValueFactory = _ => false,
            Description = "Create a relation from the copy back to the original.",
        };
        cmd.Add(idArg);
        cmd.Add(parentOpt);
        cmd.Add(descendantsOpt);
        cmd.Add(relateOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                // Object output surfaces the copy's new id (#91) so a script can chain to it, matching
                // every other create verb.
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.CopyContentAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(descendantsOpt),
                            parseResult.GetValue(relateOpt),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
