using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>document-type list</c> command.</summary>
public static class ContentTypesListCommand
{
    /// <summary>
    /// Builds the <c>document-type list</c> command. Backed by the document-type tree, walked
    /// through its folders; the tree items carry no alias, so the client reads each type by id to
    /// fill it in, as <c>media-type list</c> and <c>member-type list</c> do.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all document types defined in the Umbraco instance."
        ).WithExamples(
            "umbraco document-type list",
            "umbraco document-type list --output json | jq '.data[].alias'"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetDocumentTypesAsync(skip, take, c),
                    // The alias column was left out while the list could not fill it (#75); the
                    // client now reads it for each type on the page (#221, #416), so the table shows it again.
                    ["ID", "Name", "Alias", "IsElement"],
                    i => new[] { i.Id.ToString(), i.Name, i.Alias, i.IsElement.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
