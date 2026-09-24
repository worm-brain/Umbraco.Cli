using System.CommandLine;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-types list</c> command (issue #56).</summary>
public static class MemberTypesListCommand
{
    /// <summary>
    /// Builds the <c>member-types list</c> command. Backed by the member-type tree root, which
    /// exposes id/name/icon per item; alias and description require a single-item <c>get</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List member types defined in the Umbraco instance.\n\nExamples:\n  umbraco member-types list\n  umbraco member-types list --output json | jq '.[].name'"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "member-types.list",
                    (client, skip, take, c) => client.GetMemberTypesAsync(skip, take, c),
                    ["ID", "Name", "Icon"],
                    i => new[] { i.Id.ToString(), i.Name, i.Icon ?? "" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
