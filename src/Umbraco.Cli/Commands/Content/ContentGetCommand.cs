using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

public static class ContentGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a content item by id, including all property values."
        ).WithExamples(
            "umbraco content get 3f7a8b2e-1234-5678-abcd-ef0123456789",
            "umbraco content get <id> -o json | jq .data.values"
        );
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        cmd.Add(idArg);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetContentByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );

        return cmd;
    }
}
