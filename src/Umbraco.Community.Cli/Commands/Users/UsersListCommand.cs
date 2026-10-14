using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Users;

/// <summary>Wires the <c>user list</c> command.</summary>
public static class UsersListCommand
{
    /// <summary>Builds the <c>user list</c> command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("list", "List Umbraco backoffice users.").WithExamples(
            "umbraco user list",
            "umbraco user list --take 20 --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetUsersAsync(skip, take, c),
                    ["ID", "Name", "Email", "State"],
                    i => new[] { i.Id.ToString(), i.Name, i.Email, i.State },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
