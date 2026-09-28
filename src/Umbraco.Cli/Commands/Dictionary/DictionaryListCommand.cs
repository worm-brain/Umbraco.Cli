using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>Wires the <c>dictionary list</c> command: one level of the dictionary.</summary>
public static class DictionaryListCommand
{
    /// <summary>
    /// Builds <c>dictionary list</c>: the root level, or the direct children of <c>--parent</c> -
    /// what <c>list --parent</c> means everywhere (docs/conventions.md 2). The recursive walk is
    /// <c>dictionary tree</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List dictionary items: the root level, or the direct children of --parent."
        ).WithExamples(
            "umbraco dictionary list",
            "umbraco dictionary list --parent Blog --output json"
        );
        var parentOpt = Reference.Option(
            "--parent",
            EntityKind.DictionaryItem,
            "The item whose direct children to list; lists the root level if omitted"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.Add(parentOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        parentOpt.WithResolvedOptionalAsync(
                            parseResult,
                            client,
                            parent => client.GetDictionaryTreeAsync(parent, skip, take, c),
                            c
                        ),
                    ["ID", "Name", "Parent ID", "Has Children"],
                    i =>
                        new[]
                        {
                            i.Id.ToString(),
                            i.Name,
                            i.Parent?.Id.ToString() ?? "",
                            i.HasChildren ? "yes" : "no",
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
