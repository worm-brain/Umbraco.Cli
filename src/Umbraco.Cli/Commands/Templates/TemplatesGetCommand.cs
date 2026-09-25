using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Templates;

/// <summary>Wires <c>template get</c>.</summary>
public static class TemplatesGetCommand
{
    /// <summary>
    /// Builds the command. It prints the template's verbatim Management API body (#250 Phase 5,
    /// #207), which is the shape <c>template update --json-body</c> takes back, so a
    /// get -&gt; edit -&gt; update round-trip loses nothing.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a template by its alias or id, including the view file content.\n\nExamples:\n  umbraco template get master\n  umbraco template get master -o json | jq -r .data.content"
        );
        var idArg = Reference.Argument(EntityKind.Template, "alias");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunGetAsync(executor, parseResult, SchemaNoun.Templates, idArg, ct)
        );

        return cmd;
    }
}
