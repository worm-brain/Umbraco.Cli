using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// Wires <c>media folder</c> (#171).
/// <para>
/// <c>media upload --parent</c> takes a folder id, but nothing could create one, so organising
/// uploaded media meant opening the back office or calling the Management API directly.
/// </para>
/// </summary>
public static class MediaFolderCommand
{
    /// <summary>Builds the <c>folder</c> sub-noun.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("folder", "Create media folders to organise uploads.");
        cmd.Add(BuildCreate(executor));
        return cmd;
    }

    /// <summary>Builds <c>media folder create</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a media folder.\n\nA folder is an ordinary media item of the Folder media type, so it is deleted, moved and trashed with the usual media verbs.\n\nExamples:\n  umbraco media folder create --name Blog\n  umbraco media folder create --name 2026 --parent 3f7a8b2e-..."
        ).Mutating();
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "Folder name.",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent folder UUID. Creates at the media root when omitted.",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(nameOpt);
        cmd.Add(parentOpt);
        cmd.Add(idOpt);

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        client.CreateMediaFolderAsync(
                            parseResult.GetValue(nameOpt)!,
                            parseResult.GetValue(parentOpt),
                            parseResult.GetValue(idOpt),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
