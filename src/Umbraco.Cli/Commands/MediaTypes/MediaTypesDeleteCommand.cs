using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MediaTypes;

/// <summary>Wires the <c>media-type delete</c> command (issue #55).</summary>
public static class MediaTypesDeleteCommand
{
    /// <summary>
    /// Builds the <c>media-type delete</c> command: destructive, gated by confirmation, and
    /// refused before confirmation unless <c>--force</c> while anything uses the type (#253, #287):
    /// Umbraco deletes every media item of the type with it, so the items are counted (the recycle
    /// bin included), and a type used as a composition is refused too.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a media type by id or alias.\n\n"
                + "Umbraco deletes every media item of this type along with it. The delete is "
                + "refused unless --force is given while any media item uses the type (the recycle "
                + "bin included) or another type uses it as a composition.\n\n"
                + "Examples:\n  umbraco media-type delete brochure --yes\n"
                + "  umbraco media-type delete brochure --force --yes"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.MediaType);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            idArg,
            "Delete the media type even while media items or other types use it."
        );
        cmd.Destructive(parseResult =>
            $"Permanently delete media type {parseResult.GetValue(idArg)}? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteMediaTypeAsync(id, c).Then(ItemRef.Of(id)),
                            c
                        ),
                    "Media type deleted.",
                    ct
                )
        );

        return cmd;
    }
}
