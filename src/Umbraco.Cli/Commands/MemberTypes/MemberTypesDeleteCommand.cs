using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types delete</c> command (issue #56).</summary>
public static class MemberTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>member-types delete</c> command: destructive, gated by confirmation, and
    /// refused before confirmation while the type still has members, unless <c>--force</c>,
    /// because Umbraco deletes them with it (#253).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a member type by id or alias.\n\n"
                + "A member type that still has members is refused unless --force is given: Umbraco "
                + "deletes its members with it.\n\n"
                + "Example:\n  umbraco member-types delete siteMember --yes"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.MemberType);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            SchemaKinds.MemberType,
            idArg,
            "Delete the member type even though it has members, deleting them too."
        );
        cmd.Destructive(parseResult =>
            $"Permanently delete member type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteMemberTypeAsync(id, c),
                            c
                        ),
                    "Member type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
