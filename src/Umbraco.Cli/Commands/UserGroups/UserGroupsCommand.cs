using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.UserGroups;

/// <summary>
/// Wires the <c>user-group</c> noun (issue #109) and its verbs: list/get/create/update/delete,
/// delete of one or several groups, and add-users/remove-users membership. Granular per-node permissions are a
/// deferred follow-up, so create/update expose only the scalar and string-list fields as options.
/// </summary>
public static class UserGroupsCommand
{
    /// <summary>Builds the <c>user-group</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "user-group",
            "List, inspect, and manage Umbraco user groups.\n\nExamples:\n  umbraco user-group list\n  umbraco user-group create --alias editors --name Editors --section Umb.Section.Content"
        );
        cmd.Add(BuildList(executor));
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildCreate(executor));
        cmd.Add(BuildUpdate(executor));
        cmd.Add(BuildDelete(executor));
        cmd.Add(BuildAddUsers(executor));
        cmd.Add(BuildRemoveUsers(executor));
        return cmd;
    }

    private static Command BuildList(CommandExecutor executor)
    {
        var cmd = new Command(
            "list",
            "List user groups.\n\nExamples:\n  umbraco user-group list\n  umbraco user-group list --output json"
        );
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
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
            "Get a user group by id, alias or name.\n\nExamples:\n  umbraco user-group get blogEditors"
        );
        var idArg = Reference.Argument(EntityKind.UserGroup);
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
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
            "Create a user group.\n\nExamples:\n  umbraco user-group create --alias editors --name Editors --section Umb.Section.Content --fallback-permission Umb.Document.Read"
        ).Mutating();
        var aliasOpt = new Option<string>("--alias")
        {
            Required = true,
            Description = "Unique group alias.",
        };
        var nameOpt = new Option<string>("--name") { Required = true, Description = "Group name." };
        var shared = new SharedGroupOptions();
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
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
                                HasAccessToAllLanguages = s.HasAccessToAllLanguages ?? false,
                                DocumentRootAccess = s.DocumentRootAccess ?? false,
                                MediaRootAccess = s.MediaRootAccess ?? false,
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

    /// <summary>
    /// Builds <c>user-group update</c>: reads the group, lays the given options over it and writes
    /// it back, so an omitted option keeps its value (docs/conventions.md 5.1). The API itself takes
    /// the whole group, which is why this used to reset everything not passed.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildUpdate(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a user group by id, alias or name. Omitted options keep their values.\n\nExamples:\n  umbraco user-group update editors --name \"Site editors\"\n  umbraco user-group update editors --section Umb.Section.Media --document-start-node <id>"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.UserGroup);
        var aliasOpt = new Option<string?>("--alias") { Description = "New group alias." };
        var nameOpt = new Option<string?>("--name") { Description = "New group name." };
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
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            async id =>
                            {
                                var current = await client.GetUserGroupByIdAsync(id, c);
                                if (!current.IsSuccess)
                                    return UmbracoResponse<UserGroupResponse>.FailureFrom(current);
                                var request = Merge(
                                    current.Data!,
                                    parseResult.GetValue(aliasOpt),
                                    parseResult.GetValue(nameOpt),
                                    s
                                );
                                // An update's data is the resulting group, as get shows it.
                                return await client
                                    .UpdateUserGroupAsync(id, request, c)
                                    .ThenRead(() => client.GetUserGroupByIdAsync(id, c));
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

    /// <summary>
    /// The update request for a group: each given value over the current one. Lists given replace
    /// the list; a start node and root access say opposite things, so setting one clears the other.
    /// </summary>
    /// <param name="current">The group as it is now.</param>
    /// <param name="alias">The new alias, or null to keep it.</param>
    /// <param name="name">The new name, or null to keep it.</param>
    /// <param name="given">The shared options as parsed; null or empty means not given.</param>
    /// <returns>The request to send.</returns>
    internal static UpdateUserGroupRequest Merge(
        UserGroupResponse current,
        string? alias,
        string? name,
        SharedGroupValues given
    )
    {
        var documentRoot =
            given.DocumentRootAccess
            ?? (given.DocumentStartNode is not null ? false : current.DocumentRootAccess);
        var mediaRoot =
            given.MediaRootAccess
            ?? (given.MediaStartNode is not null ? false : current.MediaRootAccess);
        return new UpdateUserGroupRequest
        {
            Alias = alias ?? current.Alias,
            Name = name ?? current.Name,
            Icon = given.Icon ?? current.Icon,
            Description = given.Description ?? current.Description,
            Sections = given.Sections.Count > 0 ? given.Sections : current.Sections,
            Languages = given.Languages.Count > 0 ? given.Languages : current.Languages,
            FallbackPermissions =
                given.FallbackPermissions.Count > 0
                    ? given.FallbackPermissions
                    : current.FallbackPermissions,
            HasAccessToAllLanguages =
                given.HasAccessToAllLanguages ?? current.HasAccessToAllLanguages,
            DocumentRootAccess = documentRoot,
            MediaRootAccess = mediaRoot,
            DocumentStartNode = documentRoot
                ? null
                : given.DocumentStartNode ?? current.DocumentStartNode,
            MediaStartNode = mediaRoot ? null : given.MediaStartNode ?? current.MediaStartNode,
        };
    }

    /// <summary>
    /// Builds <c>user-group delete &lt;id&gt;...</c>: one or more groups, by id, alias or name
    /// (docs/conventions.md 3.3 - several known targets are a variadic positional, not a
    /// <c>delete-many</c> verb).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete one or more user groups by id, alias or name.\n\n"
                + "A group with users is refused unless --force is given: they lose the sections and permissions it grants.\n\n"
                + "Examples:\n  umbraco user-group delete blogEditors\n  umbraco user-group delete blogEditors newsEditors --yes\n  umbraco user-group delete blogEditors --force --yes"
        ).Mutating();
        var idsArg = new Argument<string[]>("id")
        {
            Description = "The user groups to delete: each an id, alias or name.",
            Arity = ArgumentArity.OneOrMore,
        };
        cmd.Add(idsArg);
        InUseGuard.Protect(
            cmd,
            async (parseResult, client, ct) =>
            {
                // A reference that does not resolve is left to the delete, which reports it.
                var ids = await client.ResolveIdsAsync(
                    EntityKind.UserGroup,
                    parseResult.GetValue(idsArg)!,
                    ct
                );
                if (!ids.IsSuccess)
                    return null;
                var reasons = new List<string>();
                foreach (var id in ids.Data!)
                    if (
                        await InUseGuard.ReasonAsync(client, SchemaKinds.UserGroup, id, ct) is { } r
                    )
                        reasons.Add(r);
                return reasons.Count == 0 ? null : string.Join(" ", reasons);
            },
            "Delete even though the groups have users, taking away what the groups grant them."
        );
        cmd.Destructive(parseResult =>
            parseResult.GetValue(idsArg)! is [var only]
                ? $"Permanently delete user group {only}?"
                : $"Permanently delete {parseResult.GetValue(idsArg)!.Length} user groups?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        // Resolve every reference before deleting any, so a typo deletes nothing.
                        var ids = await client.ResolveIdsAsync(
                            EntityKind.UserGroup,
                            parseResult.GetValue(idsArg)!,
                            c
                        );
                        if (!ids.IsSuccess)
                            return UmbracoResponse<ItemRefs>.FailureFrom(ids);
                        var delete = ids.Data! is [var one]
                            ? client.DeleteUserGroupAsync(one, c)
                            : client.DeleteUserGroupsAsync([.. ids.Data!], c);
                        return await delete.Then(ItemRefs.Of(ids.Data!));
                    },
                    "User group(s) deleted.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildAddUsers(CommandExecutor executor)
    {
        var cmd = new Command(
            "add-users",
            "Add users to a user group.\n\nExamples:\n  umbraco user-group add-users blogEditors --user <guid> --user <guid>"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.UserGroup);
        var usersOpt = ListOption.Guids("--user", "User ID to add.").AsRequired();
        cmd.Add(idArg);
        cmd.Add(usersOpt);
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
                                    .AddUsersToGroupAsync(id, parseResult.GetValue(usersOpt)!, c)
                                    .Then(ItemRef.Of(id)),
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
            "Remove users from a user group.\n\nExamples:\n  umbraco user-group remove-users blogEditors --user <guid>"
        ).Mutating();
        var idArg = Reference.Argument(EntityKind.UserGroup);
        var usersOpt = ListOption.Guids("--user", "User ID to remove.").AsRequired();
        cmd.Add(idArg);
        cmd.Add(usersOpt);
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
                                    .RemoveUsersFromGroupAsync(
                                        id,
                                        parseResult.GetValue(usersOpt)!,
                                        c
                                    )
                                    .Then(ItemRef.Of(id)),
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
    internal sealed class SharedGroupOptions
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
            "--culture",
            "Culture ISO codes the group can edit."
        );
        private readonly Option<string[]> _fallback = ListOption.Strings(
            "--fallback-permission",
            "Default permission verbs applied where no node-specific permission is set."
        );
        private readonly Option<bool?> _allLanguages = new("--has-access-to-all-languages")
        {
            Description = "Grant edit access to content in every language.",
        };
        private readonly Option<bool?> _documentRoot = new("--document-root-access")
        {
            Description = "Set the content start node to the tree root.",
        };
        private readonly Option<bool?> _mediaRoot = new("--media-root-access")
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
            cmd.Validators.Add(result =>
            {
                if (
                    !result.TryGetValue(_documentStart, out var documentStart)
                    || !result.TryGetValue(_mediaStart, out var mediaStart)
                )
                    return;
                if (result.GetValue(_documentRoot) == true && documentStart is not null)
                    result.AddError(
                        $"{_documentRoot.Name} and {_documentStart.Name} cannot be used together."
                    );
                if (result.GetValue(_mediaRoot) == true && mediaStart is not null)
                    result.AddError(
                        $"{_mediaRoot.Name} and {_mediaStart.Name} cannot be used together."
                    );
            });
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
    /// <param name="HasAccessToAllLanguages">Whether the group can edit every language; null when not given.</param>
    /// <param name="DocumentRootAccess">Whether the content start node is the tree root; null when not given.</param>
    /// <param name="MediaRootAccess">Whether the media start node is the tree root; null when not given.</param>
    /// <param name="DocumentStartNode">The content start node, or null for none.</param>
    /// <param name="MediaStartNode">The media start node, or null for none.</param>
    internal readonly record struct SharedGroupValues(
        string? Icon,
        string? Description,
        IReadOnlyList<string> Sections,
        IReadOnlyList<string> Languages,
        IReadOnlyList<string> FallbackPermissions,
        bool? HasAccessToAllLanguages,
        bool? DocumentRootAccess,
        bool? MediaRootAccess,
        Guid? DocumentStartNode,
        Guid? MediaStartNode
    );
}
