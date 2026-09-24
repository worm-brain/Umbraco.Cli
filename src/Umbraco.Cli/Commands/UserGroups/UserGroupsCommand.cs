using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.UserGroups;

/// <summary>
/// Wires the <c>user-groups</c> noun (issue #109) and its verbs: list/get/create/update/delete,
/// bulk delete-many, and add-users/remove-users membership. Granular per-node permissions are a
/// deferred follow-up, so create/update expose only the scalar and string-list fields as options.
/// </summary>
public static class UserGroupsCommand
{
    /// <summary>Builds the <c>user-groups</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "user-groups",
            "List, inspect, and manage Umbraco user groups.\n\nExamples:\n  umbraco user-groups list\n  umbraco user-groups create --alias editors --name Editors --section Umb.Section.Content"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildCreate(executor));
        cmd.Add(BuildUpdate(executor));
        cmd.Add(BuildDelete(executor));
        cmd.Add(BuildDeleteMany(executor));
        cmd.Add(BuildAddUsers(executor));
        cmd.Add(BuildRemoveUsers(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command("list", "List user groups.");
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd, defaultTake: 100);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    "user-groups.list",
                    (client, skip, take, c) => client.GetUserGroupsAsync(skip, take, c),
                    new[] { "Id", "Alias", "Name", "Sections" },
                    g => new[] { g.Id.ToString(), g.Alias, g.Name, string.Join(",", g.Sections) },
                    parseResult.GetValue(skipOpt),
                    parseResult.GetValue(takeOpt),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a user group by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "User group ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "user-groups.get",
                    (client, c) => client.GetUserGroupByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a user group.\n\nExample:\n  umbraco user-groups create --alias editors --name Editors --section Umb.Section.Content --fallback-permission Umb.Document.Read"
        );
        var aliasOpt = new Option<string>("--alias")
        {
            Required = true,
            Description = "Unique group alias.",
        };
        var nameOpt = new Option<string>("--name") { Required = true, Description = "Group name." };
        var shared = new SharedGroupOptions();
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(aliasOpt);
        cmd.Add(nameOpt);
        shared.AddTo(cmd);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var s = shared.ReadInto(parseResult);
                return executor.RunObjectAsync(
                    parseResult,
                    "user-groups.create",
                    (client, c) =>
                        client.CreateUserGroupAsync(
                            new CreateUserGroupRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                Icon = s.Icon,
                                Description = s.Description,
                                Sections = s.Sections,
                                Languages = s.Languages,
                                FallbackPermissions = s.FallbackPermissions,
                                HasAccessToAllLanguages = s.HasAccessToAllLanguages,
                                DocumentRootAccess = s.DocumentRootAccess,
                                MediaRootAccess = s.MediaRootAccess,
                            },
                            c
                        ),
                    ct
                );
            }
        );
        return cmd;
    }

    private static Command BuildUpdate(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a user group by UUID. Unset options overwrite with their defaults, so pass the full desired state."
        );
        var idArg = new Argument<Guid>("id") { Description = "User group ID." };
        var aliasOpt = new Option<string>("--alias")
        {
            Required = true,
            Description = "Unique group alias.",
        };
        var nameOpt = new Option<string>("--name") { Required = true, Description = "Group name." };
        var shared = new SharedGroupOptions();
        cmd.Add(idArg);
        cmd.Add(aliasOpt);
        cmd.Add(nameOpt);
        shared.AddTo(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var s = shared.ReadInto(parseResult);
                return executor.RunMessageAsync(
                    parseResult,
                    "user-groups.update",
                    (client, c) =>
                        client.UpdateUserGroupAsync(
                            parseResult.GetValue(idArg),
                            new UpdateUserGroupRequest
                            {
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                Icon = s.Icon,
                                Description = s.Description,
                                Sections = s.Sections,
                                Languages = s.Languages,
                                FallbackPermissions = s.FallbackPermissions,
                                HasAccessToAllLanguages = s.HasAccessToAllLanguages,
                                DocumentRootAccess = s.DocumentRootAccess,
                                MediaRootAccess = s.MediaRootAccess,
                            },
                            c
                        ),
                    "User group updated.",
                    ct
                );
            }
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a user group by UUID.");
        var idArg = new Argument<Guid>("id") { Description = "User group ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.delete",
                    (client, c) => client.DeleteUserGroupAsync(parseResult.GetValue(idArg), c),
                    "User group deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete user group {parseResult.GetValue(idArg)}?"
                )
        );
        return cmd;
    }

    private static Command BuildDeleteMany(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete-many",
            "Delete several user groups in one call.\n\nExample:\n  umbraco user-groups delete-many --ids <guid> <guid>"
        );
        var idsOpt = new Option<Guid[]>("--ids")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = true,
            Description = "The user group IDs to delete.",
        };
        cmd.Add(idsOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.delete-many",
                    (client, c) => client.DeleteUserGroupsAsync(parseResult.GetValue(idsOpt)!, c),
                    "User groups deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete {parseResult.GetValue(idsOpt)!.Length} user group(s)?"
                )
        );
        return cmd;
    }

    private static Command BuildAddUsers(CommandExecutor executor)
    {
        var cmd = new Command(
            "add-users",
            "Add users to a user group.\n\nExample:\n  umbraco user-groups add-users <groupId> --user <guid> --user <guid>"
        );
        var idArg = new Argument<Guid>("id") { Description = "User group ID." };
        var usersOpt = new Option<Guid[]>("--user")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = true,
            Description = "User ID to add (repeat for several).",
        };
        cmd.Add(idArg);
        cmd.Add(usersOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.add-users",
                    (client, c) =>
                        client.AddUsersToGroupAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(usersOpt)!,
                            c
                        ),
                    "Users added to group.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildRemoveUsers(CommandExecutor executor)
    {
        var cmd = new Command(
            "remove-users",
            "Remove users from a user group.\n\nExample:\n  umbraco user-groups remove-users <groupId> --user <guid>"
        );
        var idArg = new Argument<Guid>("id") { Description = "User group ID." };
        var usersOpt = new Option<Guid[]>("--user")
        {
            Required = true,
            AllowMultipleArgumentsPerToken = true,
            Description = "User ID to remove (repeat for several).",
        };
        cmd.Add(idArg);
        cmd.Add(usersOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.remove-users",
                    (client, c) =>
                        client.RemoveUsersFromGroupAsync(
                            parseResult.GetValue(idArg),
                            parseResult.GetValue(usersOpt)!,
                            c
                        ),
                    "Users removed from group.",
                    ct
                )
        );
        return cmd;
    }

    /// <summary>
    /// The option set shared by <c>create</c> and <c>update</c> (everything except alias, name, and
    /// the create-only <c>--id</c>). Owning the options in one cohesive object - rather than a loose
    /// positional tuple threaded through several call sites - keeps the two verbs in step and removes
    /// the risk of mis-ordering interchangeable options. Granular per-node permissions are a deferred
    /// follow-up, so they are not represented here.
    /// </summary>
    private sealed class SharedGroupOptions
    {
        private readonly Option<string?> _icon = new("--icon")
        {
            Description = "Backoffice icon (e.g. icon-users).",
        };
        private readonly Option<string?> _description = new("--description")
        {
            Description = "Free-text description.",
        };
        private readonly Option<string[]> _sections = new("--section")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Section alias the group can access (repeat for several).",
        };
        private readonly Option<string[]> _languages = new("--language")
        {
            AllowMultipleArgumentsPerToken = true,
            Description = "Culture ISO code the group can edit (repeat for several).",
        };
        private readonly Option<string[]> _fallback = new("--fallback-permission")
        {
            AllowMultipleArgumentsPerToken = true,
            Description =
                "Default permission verb applied where no node-specific permission is set.",
        };
        private readonly Option<bool> _allLanguages = new("--has-access-to-all-languages")
        {
            Description = "Grant edit access to content in every language.",
        };
        private readonly Option<bool> _documentRoot = new("--document-root-access")
        {
            Description = "Set the content start node to the tree root.",
        };
        private readonly Option<bool> _mediaRoot = new("--media-root-access")
        {
            Description = "Set the media start node to the tree root.",
        };

        /// <summary>Adds every shared option to a command.</summary>
        /// <param name="cmd">The command to add the options to.</param>
        public void AddTo(Command cmd)
        {
            cmd.Add(_icon);
            cmd.Add(_description);
            cmd.Add(_sections);
            cmd.Add(_languages);
            cmd.Add(_fallback);
            cmd.Add(_allLanguages);
            cmd.Add(_documentRoot);
            cmd.Add(_mediaRoot);
        }

        /// <summary>Reads the shared option values off a parsed command line.</summary>
        /// <param name="parseResult">The parsed command line.</param>
        /// <returns>The shared field values, ready to copy into a create/update request.</returns>
        public SharedGroupValues ReadInto(ParseResult parseResult) =>
            new(
                parseResult.GetValue(_icon),
                parseResult.GetValue(_description),
                parseResult.GetValue(_sections) ?? [],
                parseResult.GetValue(_languages) ?? [],
                parseResult.GetValue(_fallback) ?? [],
                parseResult.GetValue(_allLanguages),
                parseResult.GetValue(_documentRoot),
                parseResult.GetValue(_mediaRoot)
            );
    }

    /// <summary>The parsed values of the shared create/update options.</summary>
    /// <param name="Icon">The backoffice icon.</param>
    /// <param name="Description">The free-text description.</param>
    /// <param name="Sections">The section aliases the group can access.</param>
    /// <param name="Languages">The culture ISO codes the group can edit.</param>
    /// <param name="FallbackPermissions">The default permission verbs.</param>
    /// <param name="HasAccessToAllLanguages">Whether the group can edit every language.</param>
    /// <param name="DocumentRootAccess">Whether the content start node is the tree root.</param>
    /// <param name="MediaRootAccess">Whether the media start node is the tree root.</param>
    private readonly record struct SharedGroupValues(
        string? Icon,
        string? Description,
        IReadOnlyList<string> Sections,
        IReadOnlyList<string> Languages,
        IReadOnlyList<string> FallbackPermissions,
        bool HasAccessToAllLanguages,
        bool DocumentRootAccess,
        bool MediaRootAccess
    );
}
