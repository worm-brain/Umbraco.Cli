using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-type create</c> command (issue #55).</summary>
public static class MediaTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>media-type create</c> command. The API requires an <c>icon</c> and a full
    /// field set (issue #47 parity); it defaults to a generic image icon and can be overridden
    /// with <c>--icon</c>. All other API-required fields are defaulted by
    /// <see cref="CreateMediaTypeRequest"/>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new media type with a given name and alias.\n\nExamples:\n  umbraco media-type create --name \"Custom Image\" --alias customImage\n  umbraco media-type create --name \"Widget\" --alias widget --is-element\n  umbraco media-type create --name \"Doc\" --alias doc --allow-at-root --icon icon-document"
        ).Mutating();
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "Display name of the new media type. Required unless --json-body is given.",
        };
        var aliasOpt = new Option<string>("--alias")
        {
            Description =
                "Alias of the new media type, e.g. blogPost. Required unless --json-body is given.",
        };
        var descOpt = new Option<string?>("--description")
        {
            Description = "Optional description shown in the backoffice.",
        };
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-picture",
            Description = "Backoffice icon alias (e.g. icon-picture, icon-document).",
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
        var idOpt = ContentTypes.ContentTypesCreateCommand.IdOption();
        // #213/#221: a --json-body carries properties and groups the flags cannot.
        var body = RawBodyCommand.AddCreateOptions(cmd, SchemaNoun.MediaTypes, nameOpt, aliasOpt);
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
                    SchemaNoun.MediaTypes,
                    body,
                    idOpt,
                    (client, id, c) =>
                        client.CreateMediaTypeAsync(
                            new CreateMediaTypeRequest
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
}
