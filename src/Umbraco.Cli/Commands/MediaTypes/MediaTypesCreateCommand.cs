using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-types create</c> command (issue #55).</summary>
public static class MediaTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>media-types create</c> command. The API requires an <c>icon</c> and a full
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
            "Create a new media type with a given name and alias.\n\nExamples:\n  umbraco media-types create --name \"Custom Image\" --alias customImage\n  umbraco media-types create --name \"Widget\" --alias widget --is-element\n  umbraco media-types create --name \"Doc\" --alias doc --allow-at-root --icon icon-document"
        );
        var nameOpt = new Option<string>("--name");
        var aliasOpt = new Option<string>("--alias");
        var descOpt = new Option<string?>("--description");
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-picture",
            Description = "Backoffice icon alias (e.g. icon-picture, icon-document).",
        };
        var isElementOpt = new Option<bool>("--is-element") { DefaultValueFactory = _ => false };
        var allowRootOpt = new Option<bool>("--allow-at-root") { DefaultValueFactory = _ => false };
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
                    "media-types.create",
                    SchemaNoun.MediaTypes,
                    body,
                    idOpt,
                    (client, c) =>
                        client.CreateMediaTypeAsync(
                            new CreateMediaTypeRequest
                            {
                                Id = parseResult.GetValue(idOpt),
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
