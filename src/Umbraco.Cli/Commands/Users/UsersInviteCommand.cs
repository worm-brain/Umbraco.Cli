using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Users;

/// <summary>Builds <c>user invite</c>: send an email invitation to a new backoffice user.</summary>
public static class UsersInviteCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">Runs the command against the resolved client.</param>
    /// <returns>The <c>invite</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "invite",
            "Send an email invitation to a new backoffice user.\n\n"
                + "Umbraco sends the invitation by email, so the site must have SMTP configured; "
                + "without it the invite is refused and no user is created.\n\n"
                + "Examples:\n"
                + "  umbraco user invite --email editor@example.com --name \"Jane Smith\" --group editor\n"
                + "  umbraco user invite --email admin@example.com --name \"Bob\" --group admin --group translator --message \"Welcome to the team!\""
        ).Mutating();
        var emailOpt = new Option<string>("--email")
        {
            Required = true,
            Description = "Email address to send the invitation to.",
        };
        var nameOpt = new Option<string>("--name")
        {
            Required = true,
            Description = "Display name of the new user.",
        };
        var msgOpt = new Option<string?>("--message")
        {
            Description = "Optional personal message to include in the invitation email.",
        };
        // The client defaults the userName to the email, which Umbraco requires by default
        // (#215); --username is for sites configured to allow a different one.
        var userNameOpt = new Option<string?>("--username")
        {
            Description = "The user's login name. Defaults to the email address.",
        };
        // A new user must belong to at least one group, so the option is required.
        var groupOpt = ListOption
            .Strings(
                "--group",
                "User groups to add the user to, by alias, name or id. At least one is required."
            )
            .AsRequired();
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(msgOpt);
        cmd.Add(userNameOpt);
        cmd.Add(groupOpt);
        cmd.SetAction(
            (parseResult, ct) =>
            {
                var email = parseResult.GetValue(emailOpt)!;
                // The client resolves the --group references and defaults the userName to
                // the email, so any caller of InviteUserAsync gets both.
                return executor.RunMessageAsync(
                    parseResult,
                    (client, c) =>
                        client
                            .InviteUserAsync(
                                new InviteUserRequest
                                {
                                    Email = email,
                                    Name = parseResult.GetValue(nameOpt)!,
                                    UserName = parseResult.GetValue(userNameOpt),
                                    Message = parseResult.GetValue(msgOpt),
                                    UserGroups = parseResult.GetValue(groupOpt)!,
                                },
                                c
                            )
                            .ThenRead(() => InvitedAsync(client, email, c)),
                    $"Invitation sent to {email}.",
                    ct
                );
            }
        );

        return cmd;
    }

    /// <summary>
    /// The invited user, as <c>user get</c> shows it - a create's data is the item it made
    /// (docs/conventions.md 6.2). The invite endpoint returns no id, so the user is found by email;
    /// if it is not visible yet, the data names the email instead.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="email">The invited email address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user, or <c>{ "email": ... }</c>; a failed read carries through.</returns>
    internal static async Task<UmbracoResponse<object>> InvitedAsync(
        IUmbracoManagementClient client,
        string email,
        CancellationToken ct
    )
    {
        const int page = 100;
        for (var skip = 0; ; skip += page)
        {
            var users = await client.GetUsersAsync(skip, page, ct);
            if (!users.IsSuccess)
                return UmbracoResponse<object>.FailureFrom(users);
            var items = users.Data!.Items.ToList();
            var match = items.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)
            );
            if (match is not null)
                return UmbracoResponse<object>.Success(match);
            if (items.Count < page || skip + page >= users.Data.Total)
                return UmbracoResponse<object>.Success(new { email });
        }
    }
}
