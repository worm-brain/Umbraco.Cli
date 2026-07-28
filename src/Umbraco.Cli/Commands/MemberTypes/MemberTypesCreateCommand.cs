using System.CommandLine;
using Umbraco.Cli.Client;

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
        var nameOpt = new Option<string>("--name") { Required = true };
        var aliasOpt = new Option<string>("--alias") { Required = true };
        var descOpt = new Option<string?>("--description");
        var iconOpt = new Option<string>("--icon")
        {
            DefaultValueFactory = _ => "icon-user",
            Description = "Backoffice icon alias (e.g. icon-user).",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
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
                )
        );

        return cmd;
    }
}
