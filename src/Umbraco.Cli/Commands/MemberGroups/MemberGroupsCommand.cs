using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.MemberGroups;

/// <summary>
/// Wires the <c>member-groups</c> noun and its list/get/create/update/delete verbs (issue #107).
/// A member group is a simple named entity (id + name), so all five verbs live in this one file.
/// </summary>
public static class MemberGroupsCommand
{
    /// <summary>Builds the <c>member-groups</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "member-groups",
            "List, inspect, and manage Umbraco member groups.\n\nExamples:\n  umbraco member-groups list\n  umbraco member-groups create --name Editors"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildCreate(executor));
        cmd.Add(BuildUpdate(executor));
        cmd.Add(BuildDelete(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List member groups.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 20);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "member-groups.list",
                    (client, c) =>
                        client.GetMemberGroupsAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Id", "Name" },
                    data => (data?.Items ?? []).Select(g => new[] { g.Id.ToString(), g.Name }),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a member group by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Member group ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "member-groups.get",
                    (client, c) => client.GetMemberGroupByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a member group.\n\nExample:\n  umbraco member-groups create --name Editors"
        );
        var nameOpt = new Option<string>("--name") { Required = true, Description = "Group name." };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(nameOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "member-groups.create",
                    (client, c) =>
                        client.CreateMemberGroupAsync(
                            new CreateMemberGroupRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Name = parseResult.GetValue(nameOpt)!,
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
        var cmd = new Command("update", "Rename a member group by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Member group ID." };
        var nameOpt = new Option<string>("--name") { Required = true, Description = "New name." };
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "member-groups.update",
                    (client, c) =>
                        client.UpdateMemberGroupAsync(
                            parseResult.GetValue(idArg),
                            new UpdateMemberGroupRequest { Name = parseResult.GetValue(nameOpt)! },
                            c
                        ),
                    "Member group updated.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a member group by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "Member group ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "member-groups.delete",
                    (client, c) => client.DeleteMemberGroupAsync(parseResult.GetValue(idArg), c),
                    "Member group deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete member group {parseResult.GetValue(idArg)}?"
                )
        );
        return cmd;
    }
}
