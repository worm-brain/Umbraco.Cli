using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the read-only <c>manifest</c> noun (issue #115): list package manifests, optionally scoped
/// to public- or private-only. The opaque <c>extensions</c> payload is not shown; the table surfaces
/// the identifying metadata.
/// </summary>
public static class ManifestCommand
{
    /// <summary>Builds the <c>manifest</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "manifest",
            "List Umbraco package manifests.\n\nExamples:\n  umbraco manifest list --scope public"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List package manifests.\n\nExamples:\n  umbraco manifest list\n  umbraco manifest list --scope public"
        );
        var scopeOpt = new Option<ManifestScope>("--scope")
        {
            DefaultValueFactory = _ => ManifestScope.All,
            Description = "Which manifests to list: All (default), Public, or Private.",
        };
        cmd.Add(scopeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunCompleteListAsync(
                    parseResult,
                    (client, c) => client.GetManifestsAsync(parseResult.GetValue(scopeOpt), c),
                    new[] { "Id", "Name", "Version" },
                    m => new[] { m.Id, m.Name, m.Version },
                    ct
                )
        );
        return cmd;
    }
}
