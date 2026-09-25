using System.CommandLine;

namespace Umbraco.Cli.Commands.Members;

public static class MembersListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List Umbraco members, optionally filtered by member group.\n\nExamples:\n  umbraco members list\n  umbraco members list --group Subscribers --output json"
        );
        var groupOpt = new Option<string?>("--group");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        // Must default to a positive page size: filter/member?take=0 returns HTTP 500
        // (issue #39). Mirror the default used by the other list commands.
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(groupOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
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
