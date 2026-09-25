using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.DataTypes;

/// <summary>
/// Advanced <c>data-types</c> verbs (issue #121): is-used, referenced-by, copy, move, and a
/// <c>folder</c> sub-noun (create/get/update/delete). copy/move and folder create/update/delete are
/// writes; delete and folder delete are confirmation-gated.
/// </summary>
public static class DataTypesAdvancedCommands
{
    /// <summary>Builds the <c>is-used</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command BuildIsUsed(CommandExecutor executor)
    {
        var cmd = new Command("is-used", "Check whether a data type is used by any content type.");
        var idArg = Reference.Argument(EntityKind.DataType);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.IsDataTypeUsedAsync(id, c),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>referenced-by</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command BuildReferencedBy(CommandExecutor executor)
    {
        var cmd = new Command(
            "referenced-by",
            "List what references a data type: the properties that use it, and the items holding values in it. "
                + "Each row's 'kind' says what it is (e.g. documentTypePropertyType).\n\n"
                + "Example:\n  umbraco data-types referenced-by Textstring"
        );
        var idArg = Reference.Argument(EntityKind.DataType);
        cmd.Add(idArg);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        // #247: the list envelope like every other list, not the raw paged model inside data.
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            async id =>
                                (
                                    await client.GetDataTypeReferencedByRawAsync(id, skip, take, c)
                                ).Map(DataTypeReferenceRows.From),
                            c
                        ),
                    ["Kind", "Alias", "Name", "On"],
                    r =>
                        new[]
                        {
                            r["kind"]?.GetValue<string?>() ?? "",
                            r["alias"]?.GetValue<string?>() ?? "",
                            r["name"]?.GetValue<string?>() ?? "",
                            OwnerAlias(r),
                        },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>
    /// The owning type's alias for a property-type reference (the document, media or member type
    /// the property is on), for the human table's last column.
    /// </summary>
    /// <param name="row">A <c>referenced-by</c> row.</param>
    /// <returns>The owner's alias, or an empty string.</returns>
    private static string OwnerAlias(JsonObject row) =>
        (row["documentType"] ?? row["mediaType"] ?? row["memberType"])?[
            "alias"
        ]?.GetValue<string?>() ?? "";

    /// <summary>Builds the <c>copy</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command BuildCopy(CommandExecutor executor)
    {
        var cmd = new Command("copy", "Copy a data type, optionally under a target folder.");
        var idArg = Reference.Argument(EntityKind.DataType);
        var targetOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Destination folder UUID; omit to copy to the root. --target works too.",
        };
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        // Not destructive: a copy adds an item and removes nothing, so it needs no --yes (#247).
        // It returns the copy, so a script can chain to the new id (#247).
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.CopyDataTypeAsync(id, parseResult.GetValue(targetOpt), c),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>move</c> verb.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command BuildMove(CommandExecutor executor)
    {
        var cmd = new Command("move", "Move a data type under a folder (or to the root).");
        var idArg = Reference.Argument(EntityKind.DataType);
        var targetOpt = new Option<Guid?>("--parent", "--target")
        {
            Description = "Destination folder UUID; omit to move to the root. --target works too.",
        };
        cmd.Add(idArg);
        cmd.Add(targetOpt);
        // Not destructive: a move is reversible, like content/media/dictionary move, so it
        // needs no --yes (#247).
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.MoveDataTypeAsync(id, parseResult.GetValue(targetOpt), c),
                            c
                        ),
                    "Data type moved.",
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds the <c>folder</c> sub-noun (create/get/update/delete).</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command BuildFolder(CommandExecutor executor)
    {
        var cmd = new Command("folder", "Manage data-type folders.");
        cmd.Add(BuildFolderGet(executor));
        cmd.Add(BuildFolderCreate(executor));
        cmd.Add(BuildFolderUpdate(executor));
        cmd.Add(BuildFolderDelete(executor));
        return cmd;
    }

    private static Command BuildFolderGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a data-type folder by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Folder ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetDataTypeFolderAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildFolderCreate(CommandExecutor executor)
    {
        var cmd = new Command("create", "Create a data-type folder.");
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
                    (client, c) =>
                        client.CreateDataTypeFolderAsync(
                            new CreateDataTypeFolderRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt)!,
                                ParentId = parseResult.GetValue(parentOpt),
                            },
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildFolderUpdate(CommandExecutor executor)
    {
        var cmd = new Command("update", "Rename a data-type folder by UUID.");
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
                    (client, c) =>
                        client.UpdateDataTypeFolderAsync(
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

    private static Command BuildFolderDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a data-type folder by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Folder ID." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete data-type folder {parseResult.GetValue(idArg)}?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) => client.DeleteDataTypeFolderAsync(parseResult.GetValue(idArg), c),
                    "Folder deleted.",
                    ct
                )
        );
        return cmd;
    }
}
