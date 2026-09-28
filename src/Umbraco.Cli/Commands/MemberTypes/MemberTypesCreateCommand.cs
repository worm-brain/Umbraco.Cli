using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-type create</c> command (issue #56).</summary>
public static class MemberTypesCreateCommand
{
    /// <summary>
    /// Builds the <c>member-type create</c> command. The API requires an <c>icon</c> and a
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
            "Create a new member type with a given name and alias.\n\nExamples:\n  umbraco member-type create --name \"Author\" --alias author\n  umbraco member-type create --name \"Subscriber\" --alias subscriber --icon icon-user"
        ).Mutating();
        var nameOpt = new Option<string>("--name")
        {
            Description =
                "Display name of the new member type. Required unless --json-body is given.",
        };
        var aliasOpt = new Option<string>("--alias")
        {
            Description =
                "Alias of the new member type, e.g. author. Required unless --json-body is given.",
        };
        var descOpt = new Option<string?>("--description")
        {
            Description = "Optional description shown in the backoffice.",
        };
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-user",
            Description = "Backoffice icon alias (e.g. icon-user).",
        };
        var idOpt = ContentTypes.ContentTypesCreateCommand.IdOption();
        // #213/#221: a --json-body carries properties and groups the flags cannot.
        var body = RawBodyCommand.AddCreateOptions(cmd, SchemaNoun.MemberTypes, nameOpt, aliasOpt);
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.Add(idOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunCreateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.MemberTypes,
                    body,
                    idOpt,
                    (client, id, c) =>
                        client.CreateMemberTypeAsync(
                            new CreateMemberTypeRequest
                            {
                                Id = id,
                                Name = parseResult.GetValue(nameOpt)!,
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Description = parseResult.GetValue(descOpt),
                                Icon = parseResult.GetValue(iconOpt)!,
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
