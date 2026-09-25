using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types create</c> command (issue #56).</summary>
public static class MemberTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>member-types create</c> command. The API requires an <c>icon</c> and a
    /// full field set (issue #47 parity); it defaults to a generic member icon and can be
    /// overridden with <c>--icon</c>. All other API-required fields are defaulted by
    /// <see cref="CreateMemberTypeRequest"/>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a new member type with a given name and alias.\n\nExamples:\n  umbraco member-types create --name \"Author\" --alias author\n  umbraco member-types create --name \"Subscriber\" --alias subscriber --icon icon-user"
        );
        var body = RawBodyCommand.AddBodyOptions(cmd);
        var nameOpt = new Option<string>("--name");
        var aliasOpt = new Option<string>("--alias");
        var descOpt = new Option<string?>("--description");
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-user",
            Description = "Backoffice icon alias (e.g. icon-user).",
        };
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
                        + "Run with --schema to print a real member type as a starting point."
                );
        });

        cmd.SetAction(
            (parseResult, ct) =>
            {
                if (body.SchemaRequested(parseResult))
                    return RawBodyCommand.RunSchemaAsync(
                        executor,
                        parseResult,
                        "member-types.create",
                        client => client.GetMemberTypeIdsAsync,
                        client => client.GetMemberTypeRawAsync,
                        "member types",
                        ct
                    );

                if (body.HasBody(parseResult))
                    return executor.RunObjectAsync(
                        parseResult,
                        "member-types.create",
                        async (client, c) =>
                            await RawBodyCommand.CreateAsync(
                                await RawBodyCommand.ReadBodyAsync(body, parseResult, c),
                                parseResult.GetValue(idOpt),
                                client.CreateMemberTypeRawAsync,
                                c
                            ),
                        ct
                    );

                return executor.RunObjectAsync(
                    parseResult,
                    "member-types.create",
                    (client, c) =>
                        client.CreateMemberTypeAsync(
                            new CreateMemberTypeRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt)!,
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Description = parseResult.GetValue(descOpt),
                                Icon = parseResult.GetValue(iconOpt)!,
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
