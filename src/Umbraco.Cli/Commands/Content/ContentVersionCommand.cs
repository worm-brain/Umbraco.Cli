using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content version</c> command (#209).</summary>
public static class ContentVersionCommand
{
    /// <summary>
    /// Builds the <c>content version</c> command: read one version of a document, with its
    /// values, so it can be compared with the current document before <c>content rollback</c>.
    /// It is a sibling of <c>versions</c> (like <c>rollback</c>) rather than a <c>versions get</c>
    /// subcommand, so <c>versions</c> stays a leaf in the command catalog.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "version",
            "Show one version of a content item, with its values. Take the id from 'content versions'."
                + "\n\nExample:\n  umbraco content version 7c1d9e4a-..."
        );
        var idArg = new Argument<Guid>("version-id") { Description = "Version ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content.version",
                    (client, c) => client.GetDocumentVersionAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }
}
