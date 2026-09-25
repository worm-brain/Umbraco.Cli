using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

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
        var cmd = new Command(
            "get",
            "Get a user group by id, alias or name.\n\nExample:\n  umbraco user-groups get blogEditors"
        );
        var idArg = Reference.Argument(EntityKind.UserGroup);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "user-groups.get",
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.GetUserGroupByIdAsync(id, c),
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
                                DocumentStartNode = s.DocumentStartNode,
                                MediaStartNode = s.MediaStartNode,
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
            "Update a user group by id, alias or name. Unset options overwrite with their defaults, so pass the full desired state."
        );
        var idArg = Reference.Argument(EntityKind.UserGroup);
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
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client.UpdateUserGroupAsync(
                                    id,
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
                                        DocumentStartNode = s.DocumentStartNode,
                                        MediaStartNode = s.MediaStartNode,
                                    },
                                    c
                                ),
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
        var cmd = new Command("delete", "Delete a user group by id, alias or name.");
        var idArg = Reference.Argument(EntityKind.UserGroup);
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete user group {parseResult.GetValue(idArg)}?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.delete",
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id => client.DeleteUserGroupAsync(id, c),
                            c
                        ),
                    "User group deleted.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildDeleteMany(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete-many",
            "Delete several user groups in one call.\n\nExample:\n  umbraco user-groups delete-many --ids blogEditors newsEditors"
        );
        var idsOpt = ListOption
            .Strings("--ids", "The user groups to delete: ids, aliases or names.")
            .AsRequired();
        cmd.Add(idsOpt);
        cmd.Destructive(parseResult =>
            $"Permanently delete {parseResult.GetValue(idsOpt)!.Length} user group(s)?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.delete-many",
                    async (client, c) =>
                    {
                        // Resolve every reference before deleting any, so a typo deletes nothing.
                        var ids = await client.ResolveIdsAsync(
                            EntityKind.UserGroup,
                            parseResult.GetValue(idsOpt)!,
                            c
                        );
                        return ids.IsSuccess
                            ? await client.DeleteUserGroupsAsync([.. ids.Data!], c)
                            : UmbracoResponse<Empty>.FailureFrom(ids);
                    },
                    "User groups deleted.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildAddUsers(CommandExecutor executor)
    {
        var cmd = new Command(
            "add-users",
            "Add users to a user group.\n\nExample:\n  umbraco user-groups add-users blogEditors --user <guid> --user <guid>"
        );
        var idArg = Reference.Argument(EntityKind.UserGroup);
        var usersOpt = ListOption.Guids("--user", "User ID to add.").AsRequired();
        cmd.Add(idArg);
        cmd.Add(usersOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.add-users",
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client.AddUsersToGroupAsync(id, parseResult.GetValue(usersOpt)!, c),
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
            "Remove users from a user group.\n\nExample:\n  umbraco user-groups remove-users blogEditors --user <guid>"
        );
        var idArg = Reference.Argument(EntityKind.UserGroup);
        var usersOpt = ListOption.Guids("--user", "User ID to remove.").AsRequired();
        cmd.Add(idArg);
        cmd.Add(usersOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.remove-users",
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client.RemoveUsersFromGroupAsync(
                                    id,
                                    parseResult.GetValue(usersOpt)!,
                                    c
                                ),
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
        private readonly Option<string[]> _sections = ListOption.Strings(
            "--section",
            "Section aliases the group can access."
        );
        private readonly Option<string[]> _languages = ListOption.Strings(
            "--language",
            "Culture ISO codes the group can edit."
        );
        private readonly Option<string[]> _fallback = ListOption.Strings(
            "--fallback-permission",
            "Default permission verbs applied where no node-specific permission is set."
        );
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

        // #217: without these a group could only start at the root, so "editors limited to the
        // Blog node" was not possible.
        private readonly Option<Guid?> _documentStart = new("--document-start-node")
        {
            Description =
                "Content node id the group's content tree starts at (instead of the root).",
        };
        private readonly Option<Guid?> _mediaStart = new("--media-start-node")
        {
            Description = "Media node id the group's media tree starts at (instead of the root).",
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
            cmd.Add(_documentStart);
            cmd.Add(_mediaStart);
            // A start node and root access say opposite things; refuse rather than pick one.
            ListOption.ValidateParsed(
                cmd,
                result =>
                {
                    if (
                        result.GetValue(_documentRoot)
                        && result.GetValue(_documentStart) is not null
                    )
                        result.AddError(
                            $"{_documentRoot.Name} and {_documentStart.Name} cannot be used together."
                        );
                    if (result.GetValue(_mediaRoot) && result.GetValue(_mediaStart) is not null)
                        result.AddError(
                            $"{_mediaRoot.Name} and {_mediaStart.Name} cannot be used together."
                        );
                }
            );
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
                parseResult.GetValue(_mediaRoot),
                parseResult.GetValue(_documentStart),
                parseResult.GetValue(_mediaStart)
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
    /// <param name="DocumentStartNode">The content start node, or null for none.</param>
    /// <param name="MediaStartNode">The media start node, or null for none.</param>
    private readonly record struct SharedGroupValues(
        string? Icon,
        string? Description,
        IReadOnlyList<string> Sections,
        IReadOnlyList<string> Languages,
        IReadOnlyList<string> FallbackPermissions,
        bool HasAccessToAllLanguages,
        bool DocumentRootAccess,
        bool MediaRootAccess,
        Guid? DocumentStartNode,
        Guid? MediaStartNode
    );
}
