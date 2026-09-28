using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.ContentTypes;

/// <summary>Wires the <c>document-type create</c> command.</summary>
public static class ContentTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>document-type create</c> command. The API requires an <c>icon</c>
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
            "Create a new document type, from flags or from a full Management API body.\n\nPass the body with --json-body. --example prints a real document type (or, on a site with none, a minimal valid body) to start from."
        )
            .WithExamples(
                "umbraco document-type create --example -o json | jq .data > t.json",
                "umbraco document-type create --json-body t.json",
                "umbraco document-type create --name \"Blog Post\" --alias blogPost",
                "umbraco document-type create --name \"Widget\" --alias widget --is-element",
                "umbraco document-type create --name \"Home Page\" --alias homePage --allow-at-root --icon icon-home"
            )
            .Mutating();
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "Display name of the new document type. Required unless --json-body is given.",
        };
        var aliasOpt = new Option<string>("--alias")
        {
            Description =
                "Alias of the new document type, e.g. blogPost. Required unless --json-body is given.",
        };
        var descOpt = new Option<string?>("--description")
        {
            Description = "Optional description shown in the backoffice.",
        };
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-document",
            Description = "Backoffice icon alias (e.g. icon-document, icon-home).",
        };
        var isElementOpt = new Option<bool>("--is-element")
        {
            DefaultValueFactory = _ => false,
            Description = "Make it an element type, for use in blocks rather than as a page.",
        };
        var allowRootOpt = new Option<bool>("--allow-at-root")
        {
            DefaultValueFactory = _ => false,
            Description = "Allow items of this type at the root of the tree.",
        };
        var idOpt = IdOption();
        var body = RawBodyCommand.AddCreateOptions(
            cmd,
            SchemaNoun.DocumentTypes,
            nameOpt,
            aliasOpt
        );
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.Add(isElementOpt);
        cmd.Add(allowRootOpt);
        cmd.Add(idOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunCreateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.DocumentTypes,
                    body,
                    idOpt,
                    (client, id, c) =>
                        client.CreateDocumentTypeAsync(
                            new CreateDocumentTypeRequest
                            {
                                Id = id,
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

    /// <summary>The <c>--id</c> option shared by the schema creates.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid?> IdOption() =>
        new("--id")
        {
            Description =
                "Optional client-supplied id, so a retried create is idempotent. With --json-body it "
                + "fills the body's id, and must match it if the body has one.",
        };
}
