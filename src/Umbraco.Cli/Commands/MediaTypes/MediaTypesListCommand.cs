using System.CommandLine;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-types list</c> command (issue #55).</summary>
public static class MediaTypesListCommand
{
    /// <summary>
    /// Builds the <c>media-types list</c> command. Backed by the media-type tree root, which
    /// exposes id/name/icon per item; alias and description require a single-item <c>get</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List media types defined in the Umbraco instance.\n\nExamples:\n  umbraco media-types list\n  umbraco media-types list --output json | jq '.[].name'"
        );
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 20 };
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "media-types.list",
                    (client, c) =>
                        client.GetMediaTypesAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name", "Icon"],
                    data =>
                        data?.Items.Select(i => new[] { i.Id.ToString(), i.Name, i.Icon ?? "" })
                        ?? [],
                    ct
                )
        );

        return cmd;
    }
}
