using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>Wires the <c>content find</c> command (issue #89).</summary>
public static class ContentFindCommand
{
    /// <summary>Builds the <c>content find</c> command (locate documents by name or by path).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "find",
            "Locate content by display name (server search) or by a name path from the root."
        ).WithExamples(
            "umbraco content find --name About",
            "umbraco content find --name Team --parent <section-id>",
            "umbraco content find --path \"Home/About/Team\""
        );
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Text to match against the display name (contains, case-insensitive).",
        };
        var pathOpt = new Option<string?>("--path")
        {
            Description = "A /-separated path of node names from the root, e.g. \"Home/About\".",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "With --name, scope the search to this node's subtree.",
        };
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.Add(nameOpt);
        cmd.Add(pathOpt);
        cmd.Add(parentOpt);

        // Parse-level requirement: exactly one search mode. Both or neither is an argument error.
        cmd.Validators.Add(result =>
        {
            var hasName = !string.IsNullOrWhiteSpace(result.GetValue(nameOpt));
            var hasPath = !string.IsNullOrWhiteSpace(result.GetValue(pathOpt));
            if (hasName == hasPath)
                result.AddError("Supply exactly one of --name or --path.");
        });

        string[] headers = ["ID", "Name", "Parent ID"];
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var path = parseResult.GetValue(pathOpt);
                if (!string.IsNullOrWhiteSpace(path))
                    return executor.RunCompleteListAsync(
                        parseResult,
                        (client, c) => client.FindContentByPathAsync(path, c),
                        headers,
                        Row,
                        ct
                    );

                return executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.FindContentByNameAsync(
                            parseResult.GetValue(nameOpt)!,
                            parseResult.GetValue(parentOpt),
                            skip,
                            take,
                            c
                        ),
                    headers,
                    Row,
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                );
            }
        );

        return cmd;

        static string[] Row(ContentItemResponse i) =>
            [i.Id.ToString(), i.Name, i.Parent?.Id.ToString() ?? ""];
    }
}
