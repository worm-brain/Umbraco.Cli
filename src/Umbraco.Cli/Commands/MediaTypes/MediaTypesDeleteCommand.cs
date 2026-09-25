using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-types delete</c> command (issue #55).</summary>
public static class MediaTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>media-types delete</c> command: destructive, gated by confirmation, and
    /// refused without <c>--force</c> because Umbraco deletes every media item of the type with it
    /// and cannot say how many there are (#253).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a media type by UUID.\n\n"
                + "Umbraco deletes every media item of this type along with it, and cannot report "
                + "how many there are, so the delete is refused unless --force is given.\n\n"
                + "Example:\n  umbraco media-types delete 3f7a8b2e-... --force --yes"
        );
        var idArg = new Argument<Guid>("id");
        var forceOpt = new Option<bool>(InUseGuard.ForceOption)
        {
            Description = "Delete the media type and every media item of that type.",
        };
        cmd.Add(idArg);
        cmd.Add(forceOpt);
        cmd.Destructive(parseResult =>
            $"Permanently delete media type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media-types.delete",
                    (client, c) =>
                    {
                        var id = parseResult.GetValue(idArg);
                        InUseGuard.Refuse(InUseGuard.MediaType(id), parseResult.GetValue(forceOpt));
                        return client.DeleteMediaTypeAsync(id, c);
                    },
                    "Media type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
