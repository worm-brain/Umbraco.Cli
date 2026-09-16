using System.CommandLine;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all Razor view templates in the Umbraco instance.\n\nExample:\n  umbraco templates list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "templates.list",
                    (client, c) =>
                        client.GetTemplatesAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name", "Alias"],
                    data =>
                        data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Alias }) ?? [],
                    ct
                )
        );

        return cmd;
    }
}
