using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires <c>member-types update</c>.</summary>
public static class MemberTypesUpdateCommand
{
    /// <summary>
    /// Builds the command. The flags change the type's scalars and keep everything else; a
    /// <c>--json-body</c> (#213) carries properties and groups too, merged into the type like
    /// <c>content-types update</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a member type by id or alias. Omitted fields (and the type's properties) are preserved. With --json-body, the body's top-level keys (properties and containers included) are merged into the type; --replace sends it as the whole type.\n\nExamples:\n  umbraco member-types update siteMember --name \"Author\" --icon icon-user\n  umbraco member-types get siteMember -o json | jq .data > mt.json\n  umbraco member-types update siteMember --json-body mt.json"
        ).Mutating();
        var options = RawBodyCommand.AddUpdateOptions(cmd, SchemaNoun.MemberTypes, hasFlags: true);
        var nameOpt = new Option<string?>("--name") { Description = "New name." };
        var aliasOpt = new Option<string?>("--alias") { Description = "New alias." };
        var descOpt = new Option<string?>("--description") { Description = "New description." };
        var iconOpt = new Option<string?>("--icon")
        {
            Description = "New backoffice icon alias (e.g. icon-user).",
        };
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                RawBodyCommand.RunUpdateAsync(
                    executor,
                    parseResult,
                    SchemaNoun.MemberTypes,
                    options,
                    "Member type updated.",
                    (client, id, c) =>
                        client.UpdateMemberTypeAsync(
                            id,
                            new UpdateMemberTypeRequest
                            {
                                Name = parseResult.GetValue(nameOpt),
                                Alias = parseResult.GetValue(aliasOpt),
                                Description = parseResult.GetValue(descOpt),
                                Icon = parseResult.GetValue(iconOpt),
                            },
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
