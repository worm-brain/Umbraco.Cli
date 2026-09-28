using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Members;

public static class MembersGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a member by id.").WithExamples(
            "umbraco member get 3f7a8b2e-...",
            "umbraco member get <id> -o json | jq .data.email"
        );
        var idArg = new Argument<Guid>("id") { Description = "Member id." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetMemberByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
