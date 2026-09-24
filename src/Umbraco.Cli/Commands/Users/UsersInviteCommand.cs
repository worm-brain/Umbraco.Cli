using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Users;

/// <summary>Builds <c>users invite</c>: send an email invitation to a new back-office user.</summary>
public static class UsersInviteCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">Runs the command against the resolved client.</param>
    /// <returns>The <c>invite</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "invite",
            "Send an email invitation to a new back-office user.\n\n"
                + "Umbraco sends the invitation by email, so the site must have SMTP configured; "
                + "without it the invite is refused and no user is created.\n\n"
                + "Examples:\n"
                + "  umbraco users invite --email editor@example.com --name \"Jane Smith\" --group editor\n"
                + "  umbraco users invite --email admin@example.com --name \"Bob\" --group admin --group translator --message \"Welcome to the team!\""
        );
        var emailOpt = new Option<string>("--email") { Required = true };
        var nameOpt = new Option<string>("--name") { Required = true };
        var msgOpt = new Option<string?>("--message");
        // Umbraco refuses an invite whose userName differs from the email when it is configured
        // to use emails as usernames (the default), and refuses one with no userName at all
        // (#215). So default it to the email; --username is for sites configured otherwise.
        var userNameOpt = new Option<string?>("--username")
        {
            Description = "The user's login name. Defaults to the email address.",
        };
        // A new user must belong to at least one group, so the option is required.
        var groupOpt = new Option<string[]>("--group")
        {
            Description =
                "A user group to add the user to, by alias, name or id. Repeatable; at least one is required.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(msgOpt);
        cmd.Add(userNameOpt);
        cmd.Add(groupOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var email = parseResult.GetValue(emailOpt)!;
                return executor.RunMessageAsync(
                    parseResult,
                    "users.invite",
                    async (client, c) =>
                    {
                        var groups = await UserGroupReferences.ResolveAsync(
                            client,
                            parseResult.GetValue(groupOpt)!,
                            c
                        );
                        if (!groups.IsSuccess)
                            return UmbracoResponse<Empty>.FailureFrom(groups);

                        return await client.InviteUserAsync(
                            new InviteUserRequest
                            {
                                Email = email,
                                Name = parseResult.GetValue(nameOpt)!,
                                UserName = parseResult.GetValue(userNameOpt) ?? email,
                                Message = parseResult.GetValue(msgOpt),
                                UserGroupIds = groups
                                    .Data!.Select(id => new ReferenceById { Id = id })
                                    .ToList(),
                            },
                            c
                        );
                    },
                    $"Invitation sent to {email}.",
                    ct
                );
            }
        );

        return cmd;
    }
}
