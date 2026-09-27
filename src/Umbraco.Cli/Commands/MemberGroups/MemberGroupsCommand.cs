using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.MemberGroups;

/// <summary>
/// Wires the <c>member-group</c> noun and its list/get/create/update/delete verbs (issue #107).
/// A member group is a simple named entity (id + name), so all five verbs live in this one file.
/// </summary>
public static class MemberGroupsCommand
{
    /// <summary>Builds the <c>member-group</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "member-group",
            "List, inspect, and manage Umbraco member groups.\n\nExamples:\n  umbraco member-group list\n  umbraco member-group create --name Editors"
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
        var cmd = new Command(
            "list",
            "List member groups.\n\nExamples:\n  umbraco member-group list\n  umbraco member-group list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) => client.GetMemberGroupsAsync(skip, take, c),
                    new[] { "Id", "Name" },
                    g => new[] { g.Id.ToString(), g.Name },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Get a member group by id or name.\n\nExamples:\n  umbraco member-group get Subscribers\n  umbraco member-group get 3f7a8b2e-..."
        );
        var idArg = Reference.Argument(EntityKind.MemberGroup);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetMemberGroupByIdAsync(id, c),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a member group.\n\nExamples:\n  umbraco member-group create --name Editors"
        ).Mutating();
        var nameOpt = new Option<string>("--name") { Required = true, Description = "Group name." };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
        };
        cmd.Add(nameOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
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
        var cmd = new Command(
            "update",
            "Update a member group's name by id or name.\n\nExamples:\n  umbraco member-group update Subscribers --name \"Newsletter subscribers\""
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.MemberGroup);
        var nameOpt = new Option<string>("--name") { Required = true, Description = "New name." };
        cmd.Add(idArg);
        cmd.Add(nameOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client
                                    .UpdateMemberGroupAsync(
                                        id,
                                        new UpdateMemberGroupRequest
                                        {
                                            Name = parseResult.GetValue(nameOpt)!,
                                        },
                                        c
                                    )
                                    .Then(ItemRef.Of(id)),
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
        var cmd = new Command(
            "delete",
            "Delete a member group by id or name.\n\n"
                + "A group with members is refused unless --force is given: they lose the membership, "
                + "and access rules that name the group stop matching anyone.\n\n"
                + "Examples:\n  umbraco member-group delete Subscribers --yes\n  umbraco member-group delete Subscribers --force --yes"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.MemberGroup);
        cmd.Add(idArg);
        InUseGuard.Protect(
            cmd,
            SchemaKinds.MemberGroup,
            idArg,
            "Delete even though the group has members, removing their membership."
        );
        cmd.Destructive(parseResult =>
            $"Permanently delete member group {parseResult.GetValue(idArg)}?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteMemberGroupAsync(id, c).Then(ItemRef.Of(id)),
                            c
                        ),
                    "Member group deleted.",
                    ct
                )
        );
        return cmd;
    }
}
