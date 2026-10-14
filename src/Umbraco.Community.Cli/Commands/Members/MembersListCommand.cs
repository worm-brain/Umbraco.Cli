using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Members;

/// <summary>Wires the <c>member list</c> command.</summary>
public static class MembersListCommand
{
    /// <summary>Builds the <c>member list</c> command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List Umbraco members, optionally filtered by member group."
        ).WithExamples(
            "umbraco member list",
            "umbraco member list --group Subscribers --output json"
        );
        var groupOpt = new Option<string?>("--group")
        {
            Description = "Only members of this member group: its id or name.",
        };
        cmd.Add(groupOpt);
        // The shared default is positive, which matters here: filter/member?take=0 is an HTTP 500 (#39).
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetMembersAsync(parseResult.GetValue(groupOpt), skip, take, c),
                    ["ID", "Name", "Email", "Approved"],
                    i => new[] { i.Id.ToString(), i.Name, i.Email, i.IsApproved.ToString() },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
