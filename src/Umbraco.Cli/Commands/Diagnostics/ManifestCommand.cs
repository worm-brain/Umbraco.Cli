using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Diagnostics;

/// <summary>
/// Wires the read-only <c>manifest</c> noun (issue #115): list package manifests, optionally scoped
/// to public- or private-only. The opaque <c>extensions</c> payload is not shown; the table surfaces
/// the identifying metadata and what each package declares about the CLI (#440).
/// </summary>
public static class ManifestCommand
{
    /// <summary>Builds the <c>manifest</c> noun with its <c>list</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("manifest", "List Umbraco package manifests.").WithExamples(
            "umbraco manifest list --scope public"
        );
        cmd.Add(BuildList(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List package manifests.").WithExamples(
            "umbraco manifest list",
            "umbraco manifest list --scope public"
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
                    new[] { "Id", "Name", "Version", "CLI" },
                    m => new[] { m.Id, m.Name, m.Version, DescribeCli(m) },
                    ct
                )
        );
        return cmd;
    }

    /// <summary>
    /// The human table's CLI column: what the package declares about the CLI (#440), as
    /// <c>key=value</c> pairs, or empty when it declares nothing. Structured output carries the
    /// declarations themselves as <c>cliCapabilities</c>.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The cell text.</returns>
    internal static string DescribeCli(ManifestResponse manifest) =>
        manifest.CliCapabilities is { } capabilities
            ? string.Join(
                ", ",
                capabilities.Select(kv =>
                    // A string reads as itself; anything else (a number, an object) as its JSON.
                    kv.Value
                        is JsonValue v
                    && v.TryGetValue<string>(out var s)
                        ? $"{kv.Key}={s}"
                        : $"{kv.Key}={kv.Value?.ToJsonString() ?? "null"}"
                )
            )
            : "";
}
