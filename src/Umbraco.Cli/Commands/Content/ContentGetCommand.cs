using System.CommandLine;

namespace Umbraco.Cli.Commands.Content;

public static class ContentGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a content item by its UUID, including all property values.\n\nExample:\n  umbraco content get 3f7a8b2e-1234-5678-abcd-ef0123456789");
        var idArg = new Argument<Guid>("id") { Description = "Content item ID." };
        cmd.Add(idArg);

        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "content.get",
            (client, c) => client.GetContentByIdAsync(parseResult.GetValue(idArg), c),
            ct));

        return cmd;
    }
}
