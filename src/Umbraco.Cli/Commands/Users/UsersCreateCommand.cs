using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Users;

/// <summary>
/// Builds <c>user create</c> (#214): a backoffice user made directly, as the backoffice's "Create
/// user" does. Unlike <c>user invite</c> it sends no email, so it works on a site without SMTP.
/// </summary>
public static class UsersCreateCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">Runs the command against the resolved client.</param>
    /// <returns>The <c>create</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "create",
            "Create a backoffice user directly, without an email invitation.\n\n"
                + "Without --password the user has no password and cannot sign in until one is set "
                + "with 'user update --new-password'. If Umbraco rejects the password, the new user "
                + "is deleted again, so a failed create can be retried as it stands.\n\n"
                + "Examples:\n"
                + "  umbraco user create --email editor@example.com --name \"Jane Smith\" --group editor --password <secret>\n"
                + "  umbraco user create --email writer@example.com --name \"Bob\" --group editor --group translator"
        ).Mutating();
        var emailOpt = new Option<string>("--email")
        {
            Required = true,
            Description = "Email address of the new user.",
        };
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "Display name of the new user.",
        };
        var userNameOpt = new Option<string?>("--username")
        {
            Description = "The user's login name. Defaults to the email address.",
        };
        // A user must belong to at least one group, as for invites.
        var groupOpt = ListOption
            .Strings(
                "--group",
                "User groups to add the user to, by alias, name or id. At least one is required."
            )
            .AsRequired();
        var passwordOpt = new Option<string?>("--password")
        {
            Description =
                "The user's password. Omit it to create the user without one (they cannot sign in until it is set).",
        };
        var idOpt = new Option<Guid?>("--id")
        {
            Description = "Optional client-supplied id, so a retried create is idempotent.",
        };
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(userNameOpt);
        cmd.Add(groupOpt);
        cmd.Add(passwordOpt);
        cmd.Add(idOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        var created = await client.CreateUserAsync(
                            new CreateUserRequest
                            {
                                Id = parseResult.GetValue(idOpt),
                                Email = parseResult.GetValue(emailOpt)!,
                                Name = parseResult.GetValue(nameOpt)!,
                                UserName = parseResult.GetValue(userNameOpt),
                                UserGroups = parseResult.GetValue(groupOpt)!,
                                Password = parseResult.GetValue(passwordOpt),
                            },
                            c
                        );
                        // A create's data is the item it made, as get shows it (conventions 6.2).
                        return created.IsSuccess
                            ? await client.GetUserByIdAsync(created.Data, c)
                            : UmbracoResponse<UserResponse>.FailureFrom(created);
                    },
                    ct
                )
        );

        return cmd;
    }
}
