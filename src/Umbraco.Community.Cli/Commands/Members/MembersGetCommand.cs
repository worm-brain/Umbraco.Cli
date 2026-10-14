using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Members;

/// <summary>Wires the <c>member get</c> command.</summary>
public static class MembersGetCommand
{
    /// <summary>Builds the <c>member get</c> command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
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
