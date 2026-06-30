using System.CommandLine;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("list", "List all dictionary items in the Umbraco instance.\n\nExample:\n  umbraco dictionary list --output json");
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt); cmd.Add(takeOpt);
        cmd.SetAction((parseResult, ct) => executor.RunTableAsync(
            parseResult, "dictionary.list",
            (client, c) => client.GetDictionaryItemsAsync(parseResult.GetValue(skipOpt), parseResult.GetValue(takeOpt), c),
            ["ID", "Name"],
            data => data?.Items.Select(i => new[] { i.Id.ToString(), i.Name }) ?? [],
            ct));

        return cmd;
    }
}
