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
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "media-types.list",
                    (client, c) =>
                        client.GetMediaTypesAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    ["ID", "Name", "Icon"],
                    i => new[] { i.Id.ToString(), i.Name, i.Icon ?? "" },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );

        return cmd;
    }
}
