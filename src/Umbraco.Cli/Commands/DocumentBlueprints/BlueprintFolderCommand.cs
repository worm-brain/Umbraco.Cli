using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.DocumentBlueprints;

/// <summary>
/// Wires the <c>document-blueprint folder</c> sub-noun (issue #113): create/get/update/delete of the
/// folders that organise blueprints in the tree.
/// </summary>
public static class BlueprintFolderCommand
{
    /// <summary>Builds the <c>folder</c> sub-command.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("folder", "Manage blueprint folders.");
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildCreate(executor));
        cmd.Add(BuildUpdate(executor));
        cmd.Add(BuildDelete(executor));
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a blueprint folder by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Folder ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "document-blueprint.folder.get",
                    (client, c) => client.GetBlueprintFolderAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a blueprint folder.\n\nExample:\n  umbraco document-blueprint folder create --name \"Marketing\""
        );
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "Folder name.",
        };
        var parentOpt = new Option<Guid?>("--parent")
        {
            Description = "Parent folder UUID. Omit to create at the root.",
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
                    "document-blueprint.folder.create",
                    (client, c) =>
                        client.CreateBlueprintFolderAsync(
                            new CreateBlueprintFolderRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt)!,
                                Parent = parseResult.GetValue(parentOpt) is { } p
                                    ? new ContentParentReference { Id = p }
                                    : null,
                            },
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildUpdate(CommandExecutor executor)
    {
        var cmd = new Command("update", "Rename a blueprint folder by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Folder ID." };
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "New folder name.",
        };
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "document-blueprint.folder.update",
                    (client, c) =>
                        client.UpdateBlueprintFolderAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(nameOpt)!,
                            c
                        ),
                    "Folder updated.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a blueprint folder by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Folder ID." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete blueprint folder {parseResult.GetValue(idArg)}?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "document-blueprint.folder.delete",
                    (client, c) =>
                        client.DeleteBlueprintFolderAsync(parseResult.GetValue(idArg), c),
                    "Folder deleted.",
                    ct
                )
        );
        return cmd;
    }
}
