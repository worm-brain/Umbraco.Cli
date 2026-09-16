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
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 100 };
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "user-groups.list",
                    (client, c) =>
                        client.GetUserGroupsAsync(
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Id", "Alias", "Name", "Sections" },
                    data =>
                        (data?.Items ?? []).Select(g =>
                            new[] { g.Id.ToString(), g.Alias, g.Name, string.Join(",", g.Sections) }
                        ),
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
        var (icon, description, sections, languages, fallback, allLangs, docRoot, mediaRoot) =
            BuildSharedOptions();
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(aliasOpt);
        cmd.Add(nameOpt);
        AddSharedOptions(
            cmd,
            icon,
            description,
            sections,
            languages,
            fallback,
            allLangs,
            docRoot,
            mediaRoot
        );
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "user-groups.create",
                    (client, c) =>
                        client.CreateUserGroupAsync(
                            new CreateUserGroupRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                Icon = parseResult.GetValue(icon),
                                Description = parseResult.GetValue(description),
                                Sections = parseResult.GetValue(sections) ?? [],
                                Languages = parseResult.GetValue(languages) ?? [],
                                FallbackPermissions = parseResult.GetValue(fallback) ?? [],
                                HasAccessToAllLanguages = parseResult.GetValue(allLangs),
                                DocumentRootAccess = parseResult.GetValue(docRoot),
                                MediaRootAccess = parseResult.GetValue(mediaRoot),
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
            "Update a user group by UUID. Unset options overwrite with their defaults, so pass the full desired state."
        );
        var idArg = new Argument<Guid>("id") { Description = "User group ID." };
        var aliasOpt = new Option<string>("--alias")
        {
            Required = true,
            Description = "Unique group alias.",
        };
        var nameOpt = new Option<string>("--name") { Required = true, Description = "Group name." };
        var (icon, description, sections, languages, fallback, allLangs, docRoot, mediaRoot) =
            BuildSharedOptions();
        cmd.Add(idArg);
        cmd.Add(aliasOpt);
        cmd.Add(nameOpt);
        AddSharedOptions(
            cmd,
            icon,
            description,
            sections,
            languages,
            fallback,
            allLangs,
            docRoot,
            mediaRoot
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-groups.update",
                    (client, c) =>
                        client.UpdateUserGroupAsync(
                            parseResult.GetValue(idArg),
                            new UpdateUserGroupRequest
                            {
                                Alias = parseResult.GetValue(aliasOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                Icon = parseResult.GetValue(icon),
                                Description = parseResult.GetValue(description),
                                Sections = parseResult.GetValue(sections) ?? [],
                                Languages = parseResult.GetValue(languages) ?? [],
                                FallbackPermissions = parseResult.GetValue(fallback) ?? [],
                                HasAccessToAllLanguages = parseResult.GetValue(allLangs),
                                DocumentRootAccess = parseResult.GetValue(docRoot),
                                MediaRootAccess = parseResult.GetValue(mediaRoot),
                            },
                            c
                        ),
                    "User group updated.",
                    ct
                )
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
    /// Builds the option set shared by <c>create</c> and <c>update</c> (everything except alias,
    /// name, and the create-only <c>--id</c>). Kept in one place so the two verbs stay in step.
    /// </summary>
    /// <returns>The shared options as a tuple.</returns>
    private static (
        Option<string?> Icon,
        Option<string?> Description,
        Option<string[]> Sections,
        Option<string[]> Languages,
        Option<string[]> Fallback,
        Option<bool> AllLanguages,
        Option<bool> DocumentRoot,
        Option<bool> MediaRoot
    ) BuildSharedOptions() =>
        (
            new Option<string?>("--icon") { Description = "Backoffice icon (e.g. icon-users)." },
            new Option<string?>("--description") { Description = "Free-text description." },
            new Option<string[]>("--section")
            {
                AllowMultipleArgumentsPerToken = true,
                Description = "Section alias the group can access (repeat for several).",
            },
            new Option<string[]>("--language")
            {
                AllowMultipleArgumentsPerToken = true,
                Description = "Culture ISO code the group can edit (repeat for several).",
            },
            new Option<string[]>("--fallback-permission")
            {
                AllowMultipleArgumentsPerToken = true,
                Description =
                    "Default permission verb applied where no node-specific permission is set.",
            },
            new Option<bool>("--has-access-to-all-languages")
            {
                Description = "Grant edit access to content in every language.",
            },
            new Option<bool>("--document-root-access")
            {
                Description = "Set the content start node to the tree root.",
            },
            new Option<bool>("--media-root-access")
            {
                Description = "Set the media start node to the tree root.",
            }
        );

    /// <summary>Adds the shared option set to a command.</summary>
    /// <param name="cmd">The command to add the options to.</param>
    /// <param name="icon">The icon option.</param>
    /// <param name="description">The description option.</param>
    /// <param name="sections">The sections option.</param>
    /// <param name="languages">The languages option.</param>
    /// <param name="fallback">The fallback-permissions option.</param>
    /// <param name="allLangs">The has-access-to-all-languages option.</param>
    /// <param name="docRoot">The document-root-access option.</param>
    /// <param name="mediaRoot">The media-root-access option.</param>
    private static void AddSharedOptions(
        Command cmd,
        Option<string?> icon,
        Option<string?> description,
        Option<string[]> sections,
        Option<string[]> languages,
        Option<string[]> fallback,
        Option<bool> allLangs,
        Option<bool> docRoot,
        Option<bool> mediaRoot
    )
    {
        cmd.Add(icon);
        cmd.Add(description);
        cmd.Add(sections);
        cmd.Add(languages);
        cmd.Add(fallback);
        cmd.Add(allLangs);
        cmd.Add(docRoot);
        cmd.Add(mediaRoot);
    }
}
