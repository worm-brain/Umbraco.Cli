using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

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
        var body = RawBodyCommand.AddBodyOptions(cmd);
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
        var idOpt = new Option<Guid?>("--id")
        {
            Description =
                "Optional client-supplied UUID for an idempotent create (#86). With --json-body it "
                + "fills the body's id, and must match it if the body has one.",
        };
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.Add(isElementOpt);
        cmd.Add(allowRootOpt);
        cmd.Add(idOpt);
        // --name/--alias are required only for the flag-built create; a --json-body carries them
        // itself, and --schema builds nothing at all (as content-types create, #221/#213).
        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result) || body.HasBody(result))
                return;
            if (
                string.IsNullOrEmpty(result.GetValue(nameOpt))
                || string.IsNullOrEmpty(result.GetValue(aliasOpt))
            )
                result.AddError(
                    "Supply --name and --alias, or a full body with --json-body. "
                        + "Run with --schema to print a real media type as a starting point."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                    return RawBodyCommand.RunSchemaAsync(
                        executor,
                        parseResult,
                        "media-types.create",
                        client => client.GetMediaTypeIdsAsync,
                        client => client.GetMediaTypeRawAsync,
                        "media types",
                        ct
                    );

                if (body.HasBody(parseResult))
                    return executor.RunObjectAsync(
                        parseResult,
                        "media-types.create",
                        async (client, c) =>
                            await RawBodyCommand.CreateAsync(
                                await RawBodyCommand.ReadBodyAsync(body, parseResult, c),
                                parseResult.GetValue(idOpt),
                                client.CreateMediaTypeRawAsync,
                                c
                            ),
                        ct
                    );

                return executor.RunObjectAsync(
                    parseResult,
                    "media-types.create",
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
                );
            }
        );

        return cmd;
    }
}
