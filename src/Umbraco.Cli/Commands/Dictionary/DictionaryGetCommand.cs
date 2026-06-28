using System.CommandLine;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryGetCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a dictionary item and its translations by key.\n\nExample:\n  umbraco dictionary get Common.Search");
        var keyArg = new Argument<string>("key"); cmd.Add(keyArg);
        cmd.SetAction((parseResult, ct) => executor.RunObjectAsync(
            parseResult, "dictionary.get",
            (client, c) => client.GetDictionaryItemByKeyAsync(parseResult.GetValue(keyArg)!, c),
            ct));

        return cmd;
    }
}
