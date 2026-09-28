using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Templates;

public static class TemplatesListCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List all Razor view templates in the Umbraco instance."
        ).WithExamples("umbraco template list --output json");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetTemplatesAsync(skip, take, c),
                    ["ID", "Name", "Alias"],
                    i => new[] { i.Id.ToString(), i.Name, i.Alias },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
