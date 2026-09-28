using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberTypes;

/// <summary>Wires the <c>member-type list</c> command (issue #56).</summary>
public static class MemberTypesListCommand
{
    /// <summary>
    /// Builds the <c>member-type list</c> command. Backed by the member-type tree root, which
    /// exposes id/name/icon per item; alias and description require a single-item <c>get</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List member types defined in the Umbraco instance."
        ).WithExamples(
            "umbraco member-type list",
            "umbraco member-type list --output json | jq '.data[].name'"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
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
