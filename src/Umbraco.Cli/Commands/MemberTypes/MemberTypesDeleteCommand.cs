using System.CommandLine;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types delete</c> command (issue #56).</summary>
public static class MemberTypesDeleteCommand
{
    /// <summary>Builds the <c>member-types delete</c> command (destructive; gated by confirmation).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a member type by UUID. All members of this type must be removed first.\n\nExample:\n  umbraco member-types delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "member-types.delete",
                    (client, c) => client.DeleteMemberTypeAsync(parseResult.GetValue(idArg), c),
                    "Member type deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete member type {parseResult.GetValue(idArg)}? This cannot be undone."
                )
        );

        return cmd;
    }
}
