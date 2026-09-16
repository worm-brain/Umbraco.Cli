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
                executor.RunTableAsync(
                    parseResult,
                    "dictionary.list",
                    (client, c) =>
                        client.GetDictionaryItemsAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name"],
                    data => data?.Items.Select(i => new[] { i.Id.ToString(), i.Name }) ?? [],
                    ct
                )
        );

        return cmd;
    }
}
