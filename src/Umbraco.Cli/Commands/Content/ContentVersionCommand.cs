using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content version get</c> command (#209).</summary>
public static class ContentVersionCommand
{
    /// <summary>
    /// Builds the <c>content version get</c> command: read one version of a document, with its
    /// values, so it can be compared with the current document before <c>content version rollback</c>.
    /// It is a sibling of <c>versions</c> (like <c>rollback</c>) rather than a <c>versions get</c>
    /// subcommand, so <c>versions</c> stays a leaf in the command catalog.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get one version of a content item, with its values."
                + "\n\nTake the id from 'content version list'."
        ).WithExamples("umbraco content version get 7c1d9e4a-...");
        var idArg = new Argument<Guid>("id")
        {
            Description = "The version's id, from 'content version list'.",
        };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetDocumentVersionAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }
}
