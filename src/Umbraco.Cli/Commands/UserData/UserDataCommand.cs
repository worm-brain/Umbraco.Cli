using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.UserData;

/// <summary>
/// Wires the <c>user-data</c> noun (issue #109) and its list/get/create/update/delete verbs.
/// User data is key/value state scoped to the authenticated user. <c>update</c> is a
/// collection-level PUT that carries the entry's key in the body; the CLI takes it as the
/// positional <c>&lt;id&gt;</c>, like every other update.
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
            "List, inspect, and manage the authenticated user's key/value data.\n\nExamples:\n  umbraco user-data list --group myGroup\n  umbraco user-data create --group myGroup --identifier theme --data dark"
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
            "List user-data entries, optionally filtered by group/identifier.\n\n"
                + "Examples:\n  umbraco user-data list\n  umbraco user-data list --group myGroup --identifier theme"
        );
        var groupOpt = new Option<string?>("--group") { Description = "Filter by group." };
        var identifierOpt = new Option<string?>("--identifier")
        {
            Description = "Filter by identifier.",
        };
        cmd.Add(groupOpt);
        cmd.Add(identifierOpt);
        var (skipOpt, takeOpt) = PagingOptions.Add(cmd);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunPagedAsync(
                    parseResult,
                    (client, skip, take, c) =>
                        client.GetUserDataAsync(
                            parseResult.GetValue(groupOpt),
                            parseResult.GetValue(identifierOpt),
                            skip,
                            take,
                            c
                        ),
                    new[] { "Key", "Group", "Identifier", "Value" },
                    d => new[] { d.Key.ToString(), d.Group, d.Identifier, d.Value },
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
            "Get a user-data entry by id.\n\nExamples:\n  umbraco user-data get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "The entry's id (its key)." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
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
            "Create a user-data entry.\n\nExamples:\n  umbraco user-data create --group myGroup --identifier theme --data dark"
        ).Mutating();
        var groupOpt = new Option<string>("--group") { Required = true, Description = "Group." };
        var identifierOpt = new Option<string>("--identifier")
        {
            Required = true,
            Description = "Identifier within the group.",
        };
        // --data, not --value: across the CLI --value means an alias=value pair.
        var valueOpt = new Option<string>("--data")
        {
            Required = true,
            Description = "Value to store.",
        };
        var keyOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
        };
        cmd.Add(groupOpt);
        cmd.Add(identifierOpt);
        cmd.Add(valueOpt);
        cmd.Add(keyOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
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

    /// <summary>
    /// Builds <c>user-data update</c>: reads the entry and lays the given options over it, so an
    /// omitted one keeps its value (docs/conventions.md 5.1). The API takes the whole entry.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildUpdate(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a user-data entry by id. Omitted options keep their values.\n\nExamples:\n  umbraco user-data update <id> --data light"
        ).Mutating();
        // The id is positional, as on every other update (#242).
        var keyArg = new Argument<Guid>("id") { Description = "The entry's id (its key)." };
        var groupOpt = new Option<string?>("--group") { Description = "New group." };
        var identifierOpt = new Option<string?>("--identifier")
        {
            Description = "New identifier within the group.",
        };
        var valueOpt = new Option<string?>("--data") { Description = "New value to store." };
        cmd.Add(keyArg);
        cmd.Add(groupOpt);
        cmd.Add(identifierOpt);
        cmd.Add(valueOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        var key = parseResult.GetValue(keyArg);
                        var current = await client.GetUserDataByIdAsync(key, c);
                        if (!current.IsSuccess)
                            return UmbracoResponse<UserDataResponse>.FailureFrom(current);
                        // An update's data is the resulting entry, as get shows it.
                        return await client
                            .UpdateUserDataAsync(
                                new UpdateUserDataRequest
                                {
                                    Key = key,
                                    Group = parseResult.GetValue(groupOpt) ?? current.Data!.Group,
                                    Identifier =
                                        parseResult.GetValue(identifierOpt)
                                        ?? current.Data!.Identifier,
                                    Value = parseResult.GetValue(valueOpt) ?? current.Data!.Value,
                                },
                                c
                            )
                            .ThenRead(() => client.GetUserDataByIdAsync(key, c));
                    },
                    "User-data entry updated.",
                    ct
                )
        );
        return cmd;
    }

    private static Command BuildDelete(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete a user-data entry by id.\n\nExamples:\n  umbraco user-data delete 3f7a8b2e-... --yes"
        ).Mutating();
        var idArg = new Argument<Guid>("id") { Description = "The entry's id (its key)." };
        cmd.Add(idArg);
        cmd.Destructive(parseResult =>
            $"Permanently delete user-data entry {parseResult.GetValue(idArg)}?"
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .DeleteUserDataAsync(parseResult.GetValue(idArg), c)
                            .Then(ItemRef.Of(parseResult.GetValue(idArg))),
                    "User-data entry deleted.",
                    ct
                )
        );
        return cmd;
    }
}
