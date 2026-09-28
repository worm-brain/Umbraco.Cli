using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Users;

/// <summary>Builds <c>user get</c>: one backoffice user by id, email or username (#216).</summary>
public static class UsersGetCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">Runs the command against the resolved client.</param>
    /// <returns>The <c>get</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a backoffice user by id, email or username.\n\n"
                + "Shows the user's groups (id, alias and name), the sections they grant, start nodes, "
                + "UI language and login record.\n\n"
                + "Examples:\n  umbraco user get 3f7a8b2e-...\n  umbraco user get editor@example.com\n"
                + "  umbraco user get <id> -o json | jq .data.userGroups"
        );
        var idArg = Reference.Argument(EntityKind.User);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetUserByIdAsync(id, c),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
