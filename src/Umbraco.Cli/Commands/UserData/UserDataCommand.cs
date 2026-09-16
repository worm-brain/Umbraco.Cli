using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.UserData;

/// <summary>
/// Wires the <c>user-data</c> noun (issue #109) and its list/get/create/update/delete verbs.
/// User data is key/value state scoped to the authenticated user; <c>update</c> is a
/// collection-level PUT that carries the target key in the body, so it takes <c>--key</c>
/// rather than a positional id argument.
/// </summary>
public static class UserDataCommand
{
    /// <summary>Builds the <c>user-data</c> noun with its verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "user-data",
            "List, inspect, and manage the authenticated user's key/value data.\n\nExamples:\n  umbraco user-data list --group myGroup\n  umbraco user-data create --group myGroup --identifier theme --value dark"
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
            "List user-data entries, optionally filtered by group/identifier."
        );
        var groupOpt = new Option<string?>("--group") { Description = "Filter by group." };
        var identifierOpt = new Option<string?>("--identifier")
        {
            Description = "Filter by identifier.",
        };
        var skipOpt = new Option<int>("--skip") { DefaultValueFactory = _ => 0 };
        var takeOpt = new Option<int>("--take") { DefaultValueFactory = _ => 100 };
        cmd.Add(groupOpt);
        cmd.Add(identifierOpt);
        cmd.Add(skipOpt);
        cmd.Add(takeOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunTableAsync(
                    parseResult,
                    "user-data.list",
                    (client, c) =>
                        client.GetUserDataAsync(
                            parseResult.GetValue(groupOpt),
                            parseResult.GetValue(identifierOpt),
                            parseResult.GetValue(skipOpt),
                            parseResult.GetValue(takeOpt),
                            c
                        ),
                    new[] { "Key", "Group", "Identifier", "Value" },
                    data =>
                        (data?.Items ?? []).Select(d =>
                            new[] { d.Key.ToString(), d.Group, d.Identifier, d.Value }
                        ),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a user-data entry by key.");
        var idArg = new Argument<Guid>("key") { Description = "Entry key." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "user-data.get",
                    (client, c) => client.GetUserDataByIdAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildCreate(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a user-data entry.\n\nExample:\n  umbraco user-data create --group myGroup --identifier theme --value dark"
        );
        var groupOpt = new Option<string>("--group") { Required = true, Description = "Group." };
        var identifierOpt = new Option<string>("--identifier")
        {
            Required = true,
            Description = "Identifier within the group.",
        };
        var valueOpt = new Option<string>("--value")
        {
            Required = true,
            Description = "Value to store.",
        };
        var keyOpt = new Option<Guid?>("--key")
        {
            Description = "Optional client-supplied UUID for an idempotent create (#86).",
        };
        cmd.Add(groupOpt);
        cmd.Add(identifierOpt);
        cmd.Add(valueOpt);
        cmd.Add(keyOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "user-data.create",
                    (client, c) =>
                        client.CreateUserDataAsync(
                            new CreateUserDataRequest
                            {
                                Key = parseResult.GetValue(keyOpt),
                                Group = parseResult.GetValue(groupOpt)!,
                                Identifier = parseResult.GetValue(identifierOpt)!,
                                Value = parseResult.GetValue(valueOpt)!,
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
            "Update a user-data entry by key.\n\nExample:\n  umbraco user-data update --key <guid> --group myGroup --identifier theme --value light"
        );
        var keyOpt = new Option<Guid>("--key")
        {
            Required = true,
            Description = "Key of the entry to update.",
        };
        var groupOpt = new Option<string>("--group") { Required = true, Description = "Group." };
        var identifierOpt = new Option<string>("--identifier")
        {
            Required = true,
            Description = "Identifier within the group.",
        };
        var valueOpt = new Option<string>("--value")
        {
            Required = true,
            Description = "New value to store.",
        };
        cmd.Add(keyOpt);
        cmd.Add(groupOpt);
        cmd.Add(identifierOpt);
        cmd.Add(valueOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-data.update",
                    (client, c) =>
                        client.UpdateUserDataAsync(
                            new UpdateUserDataRequest
                            {
                                Key = parseResult.GetValue(keyOpt),
                                Group = parseResult.GetValue(groupOpt)!,
                                Identifier = parseResult.GetValue(identifierOpt)!,
                                Value = parseResult.GetValue(valueOpt)!,
                            },
                            c
                        ),
                    "User-data entry updated.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command("delete", "Delete a user-data entry by key.");
        var idArg = new Argument<Guid>("key") { Description = "Entry key." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    "user-data.delete",
                    (client, c) => client.DeleteUserDataAsync(parseResult.GetValue(idArg), c),
                    "User-data entry deleted.",
                    ct,
                    confirmationPrompt: $"Permanently delete user-data entry {parseResult.GetValue(idArg)}?"
                )
        );
        return cmd;
    }
}
