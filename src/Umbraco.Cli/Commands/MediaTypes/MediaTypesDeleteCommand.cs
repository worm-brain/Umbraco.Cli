using System.CommandLine;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-types delete</c> command (issue #55).</summary>
public static class MediaTypesDeleteCommand
{
    /// <summary>Builds the <c>media-types delete</c> command (destructive; gated by confirmation).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a media type by UUID. All media of this type must be removed first.\n\nExample:\n  umbraco media-types delete 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id");
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "media-types.delete",
                    (client, c) => client.DeleteMediaTypeAsync(parseResult.GetValue(idArg), c),
                    "Media type deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete media type {parseResult.GetValue(idArg)}? This cannot be undone."
                )
        );

        return cmd;
    }
}
