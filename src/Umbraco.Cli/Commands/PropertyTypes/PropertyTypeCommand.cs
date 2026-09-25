using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.PropertyTypes;

/// <summary>
/// Wires the read-only <c>property-type</c> noun (issue #121): check whether a property (by content
/// type and alias) is in use. There is no <c>element</c> resource in the API - "element" is a flag on
/// document/media types, not its own endpoint - so this noun covers the property-type usage check.
/// </summary>
public static class PropertyTypeCommand
{
    /// <summary>Builds the <c>property-type</c> noun with its <c>is-used</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "property-type",
            "Inspect property-type usage.\n\nExample:\n  umbraco property-type is-used --content-type blogPost --alias bodyText"
        );
        cmd.Add(BuildIsUsed(executor));
        return cmd;
    }

    private static Command BuildIsUsed(CommandExecutor executor)
    {
        var cmd = new Command("is-used", "Check whether a property is in use.");
        var contentTypeOpt = Reference
            .Option(
                "--content-type",
                EntityKind.DocumentType,
                "The document type the property belongs to"
            )
            .AsRequired();
        var aliasOpt = new Option<string>("--alias")
        {
            Required = true,
            Description = "The property alias.",
        };
        cmd.Add(contentTypeOpt);
        cmd.Add(aliasOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        contentTypeOpt.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client.IsPropertyTypeUsedAsync(
                                    id,
                                    parseResult.GetValue(aliasOpt)!,
                                    c
                                ),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }
}
