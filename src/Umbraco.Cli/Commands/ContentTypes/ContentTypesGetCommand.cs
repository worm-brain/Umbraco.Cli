using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires <c>content-types get</c>.</summary>
public static class ContentTypesGetCommand
{
    /// <summary>
    /// Builds the command. It prints the document type's verbatim Management API body (#250 Phase 5,
    /// #201), which is the shape <c>content-types update --json-body</c> takes back, so a
    /// get -&gt; edit -&gt; update round-trip loses nothing.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a document type by alias or UUID: the full Management API body, with its properties, property groups, allowed templates and child types, compositions and list view. The output is a valid 'update --json-body'.\n\nExamples:\n  umbraco content-types get blogPost\n  umbraco content-types get blogPost -o json | jq .data > t.json"
        );
        var idArg = Reference.Argument(EntityKind.DocumentType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunGetAsync(
                    executor,
                    parseResult,
                    "content-types.get",
                    idArg,
                    client => client.GetDocumentTypeRawAsync,
                    ct
                )
        );

        return cmd;
    }
}
