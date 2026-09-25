using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>
/// Wires <c>content-types update</c> (#161).
/// <para>
/// There was no update verb at all, so changing a document type - adding a property, a group, a
/// template, or turning on culture variance - meant exporting the whole schema, editing it with
/// <c>jq</c>, and applying it back. This takes the type's own body directly.
/// </para>
/// </summary>
public static class ContentTypesUpdateCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a document type from a Management API body - its properties, groups, compositions, allowed templates and culture variance.\n\nThe body's top-level keys are merged into the type, so a key you leave out keeps its value; --replace sends the body as the whole type. Read it with get, edit, write it back:\n\nExamples:\n  umbraco content-types get blogPost -o json | jq .data > t.json\n  # ...edit t.json...\n  umbraco content-types update blogPost --json-body t.json\n  umbraco content-types update --schema"
        ).Mutating();
        var options = RawBodyCommand.AddUpdateOptions(
            cmd,
            SchemaNoun.DocumentTypes,
            hasFlags: false
        );
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunUpdateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.DocumentTypes,
                    options,
                    "Document type updated.",
                    ct
                )
        );

        return cmd;
    }
}
