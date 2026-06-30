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
        var skipOpt = new Option<int>("--skip");
        var takeOpt = new Option<int>("--take");
        cmd.Add(groupOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "members.list",
                    (client, c) =>
                        client.GetMembersAsync(
                            parseResult.GetValue(groupOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name", "Email", "Approved"],
                    data =>
                        data?.Items.Select(i =>
                            new[] { i.Id.ToString(), i.Name, i.Email, i.IsApproved.ToString() }
                        )
                        ?? [],
                    ct
                )
        );

        return cmd;
    }
}
