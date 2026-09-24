using System.CommandLine;

namespace Umbraco.Cli.Commands.Dictionary;

public static class DictionaryListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all dictionary items in the Umbraco instance.\n\nExample:\n  umbraco dictionary list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "dictionary.list",
                    (client, skip, take, c) => client.GetDictionaryItemsAsync(skip, take, c),
                    ["ID", "Name"],
                    i => new[] { i.Id.ToString(), i.Name },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
