using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types delete</c> command (issue #56).</summary>
public static class MemberTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>member-types delete</c> command: destructive, gated by confirmation, and
    /// refused without <c>--force</c> while the type still has members, because Umbraco deletes
    /// them with it (#253).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a member type by UUID.\n\n"
                + "A member type that still has members is refused unless --force is given: Umbraco "
                + "deletes its members with it.\n\n"
                + "Example:\n  umbraco member-types delete 3f7a8b2e-... --yes"
        );
        var idArg = new Argument<Guid>("id");
        var forceOpt = new Option<bool>(InUseGuard.ForceOption)
        {
            Description = "Delete the member type even though it has members, deleting them too.",
        };
        cmd.Add(idArg);
        cmd.Add(forceOpt);
        cmd.Destructive(parseResult =>
            $"Permanently delete member type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "member-types.delete",
                    async (client, c) =>
                    {
                        var id = parseResult.GetValue(idArg);
                        InUseGuard.Refuse(
                            await InUseGuard.MemberTypeAsync(client, id, c),
                            parseResult.GetValue(forceOpt)
                        );
                        return await client.DeleteMemberTypeAsync(id, c);
                    },
                    "Member type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
