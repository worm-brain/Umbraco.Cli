using System.CommandLine;
using Umbraco.Community.Cli.Infrastructure;

namespace Umbraco.Community.Cli.Commands.Templates;

/// <summary>Wires the <c>template list</c> command.</summary>
public static class TemplatesListCommand
{
    /// <summary>
    /// Builds the <c>template list</c> command: every template, including those nested under a master.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
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
