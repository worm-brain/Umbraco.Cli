using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-type list</c> command (issue #55).</summary>
public static class MediaTypesListCommand
{
    /// <summary>
    /// Builds the <c>media-type list</c> command. Backed by the media-type tree root, which
    /// exposes id/name/icon per item; alias and description require a single-item <c>get</c>.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List media types defined in the Umbraco instance."
        ).WithExamples(
            "umbraco media-type list",
            "umbraco media-type list --output json | jq '.data[].name'"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetMediaTypesAsync(skip, take, c),
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
