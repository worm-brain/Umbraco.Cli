using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Users;

/// <summary>
/// Builds <c>user delete &lt;id&gt;...</c> (#216): one or more users, by id, email or username
/// (docs/conventions.md 3.3 - several known targets are a variadic positional).
/// </summary>
public static class UsersDeleteCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">Runs the command against the resolved client.</param>
    /// <returns>The <c>delete</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "delete",
            "Delete one or more backoffice users permanently, by id, email or username.\n\n"
                + "Umbraco may refuse to delete a user who has signed in; disable them instead with "
                + "'user update --disabled'."
        )
            .WithExamples(
                "umbraco user delete editor@example.com",
                "umbraco user delete 3f7a8b2e-... writer@example.com --yes"
            )
            .Mutating();
        var idsArg = new Argument<string[]>("id")
        {
            Description = "The users to delete: each an id, email or username.",
            Arity = ArgumentArity.OneOrMore,
        };
        cmd.Add(idsArg);
        cmd.Destructive(parseResult =>
            parseResult.GetValue(idsArg)! is [var only]
                ? $"Permanently delete user {only}? This cannot be undone."
                : $"Permanently delete {parseResult.GetValue(idsArg)!.Length} users? This cannot be undone."
        );
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunMessageAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        // Resolve every reference before deleting any, so a typo deletes nothing.
                        var ids = await client.ResolveIdsAsync(
                            EntityKind.User,
                            parseResult.GetValue(idsArg)!,
                            c
                        );
                        if (!ids.IsSuccess)
                            return UmbracoResponse<ItemRefs>.FailureFrom(ids);
                        var delete = ids.Data! is [var one]
                            ? client.DeleteUserAsync(one, c)
                            : client.DeleteUsersAsync([.. ids.Data!], c);
                        return await delete.Then(ItemRefs.Of(ids.Data!));
                    },
                    "User(s) deleted.",
                    ct
                )
        );

        return cmd;
    }
}
