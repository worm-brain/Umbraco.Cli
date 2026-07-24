using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>content-types create</c> command.</summary>
public static class ContentTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>content-types create</c> command. The API requires an <c>icon</c>
    /// (issue #47); it defaults to a generic document icon and can be overridden with
    /// <c>--icon</c>. All other API-required fields (varies-by flags, cleanup policy,
    /// allowed-* collections) are defaulted by <see cref="CreateDocumentTypeRequest"/>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new document type with a given name and alias.\n\nExamples:\n  umbraco content-types create --name \"Blog Post\" --alias blogPost\n  umbraco content-types create --name \"Widget\" --alias widget --is-element\n  umbraco content-types create --name \"Home Page\" --alias homePage --allow-at-root --icon icon-home"
        );
        var nameOpt = new Option<string>("--name") { Required = true };
        var aliasOpt = new Option<string>("--alias") { Required = true };
        var descOpt = new Option<string?>("--description");
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-document",
            Description = "Backoffice icon alias (e.g. icon-document, icon-home).",
        };
        var isElementOpt = new Option<bool>("--is-element") { DefaultValueFactory = _ => false };
        var allowRootOpt = new Option<bool>("--allow-at-root") { DefaultValueFactory = _ => false };
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.Add(isElementOpt);
        cmd.Add(allowRootOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content-types.create",
                    (client, c) =>
                        client.CreateDocumentTypeAsync(
                            new CreateDocumentTypeRequest
                            {
                                Name = parseResult.GetValue(nameOpt)!,
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Description = parseResult.GetValue(descOpt),
                                Icon = parseResult.GetValue(iconOpt)!,
                                IsElement = parseResult.GetValue(isElementOpt),
                                AllowedAsRoot = parseResult.GetValue(allowRootOpt),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
