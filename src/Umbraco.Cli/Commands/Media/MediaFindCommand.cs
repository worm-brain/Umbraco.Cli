using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Media;

/// <summary>Wires the <c>media find</c> command (issue #89).</summary>
public static class MediaFindCommand
{
    /// <summary>Builds the <c>media find</c> command (locate media by name or by path).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "find",
            "Locate media by display name (server search) or by a name path from the root.\n\nExamples:\n  umbraco media find --name logo\n  umbraco media find --name hero --parent <folder-id>\n  umbraco media find --path \"Images/Logos/Primary\""
        );
        var nameOpt = new Option<string?>("--name")
        {
            Description = "Text to match against the display name (contains, case-insensitive).",
        };
        var pathOpt = new Option<string?>("--path")
        {
            Description = "A /-separated path of node names from the root, e.g. \"Images/Logos\".",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "With --name, scope the search to this folder's subtree.",
        };
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
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
                    return executor.RunTableAsync(
                        parseResult,
                        "media.find",
                        (client, c) => client.FindMediaByPathAsync(path, c),
                        headers,
                        data => data?.Select(Row) ?? [],
                        ct
                    );

                return executor.RunTableAsync(
                    parseResult,
                    "media.find",
                    (client, c) =>
                        client.FindMediaByNameAsync(
                            parseResult.GetValue(nameOpt)!,
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    headers,
                    data => data?.Items.Select(Row) ?? [],
                    ct
                );
            }
        );

        return cmd;

        static string[] Row(MediaItemResponse i) =>
            [i.Id.ToString(), i.Name, i.Parent?.Id.ToString() ?? ""];
    }
}
