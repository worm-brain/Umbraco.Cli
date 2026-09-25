using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>Wires <c>data-type get</c>.</summary>
public static class DataTypesGetCommand
{
    /// <summary>
    /// Builds the command. It prints the data type's verbatim Management API body (#250 Phase 5,
    /// #201), which is the shape <c>data-type update --json-body</c> takes back, so a
    /// get -&gt; edit -&gt; update round-trip loses nothing.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a data type by name or id, as the full Management API body.\n\nThe body includes its editor configuration. The output is a valid 'update --json-body'.\n\nA data type has no alias - 'editorAlias' names the property editor behind it, which many data types share - so the human-facing key is its name.\n\nExamples:\n  umbraco data-type get Textstring\n  umbraco data-type get 3f7a8b2e-..."
        );
        var idArg = Reference.Argument(EntityKind.DataType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunGetAsync(executor, parseResult, SchemaNoun.DataTypes, idArg, ct)
        );

        return cmd;
    }
}
