using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types update</c> command (issue #56).</summary>
public static class MemberTypesUpdateCommand
{
    /// <summary>
    /// Builds the <c>member-types update</c> command. Only supplied options change; anything
    /// omitted - including the type's properties, containers and compositions, which are never
    /// exposed here - is preserved by the client's raw-JSON read-merge.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a member type by id or alias. Omitted fields (and the type's properties) are preserved.\n\nExample:\n  umbraco member-types update siteMember --name \"Author\" --icon icon-user"
        );
        var idArg = Reference.Argument(EntityKind.MemberType);
        var nameOpt = new Option<string?>("--name") { Description = "New name." };
        var aliasOpt = new Option<string?>("--alias") { Description = "New alias." };
        var descOpt = new Option<string?>("--description") { Description = "New description." };
        var iconOpt = new Option<string?>("--icon")
        {
            Description = "New backoffice icon alias (e.g. icon-user).",
        };
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.Add(aliasOpt);
        cmd.Add(descOpt);
        cmd.Add(iconOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "member-types.update",
                    (client, c) =>
                        client.WithResolvedAsync(
                            EntityKind.MemberType,
                            parseResult.GetValue(idArg)!,
                            id =>
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
                            c
                        ),
                    "Member type updated.",
                    ct
                )
        );

        return cmd;
    }
}
