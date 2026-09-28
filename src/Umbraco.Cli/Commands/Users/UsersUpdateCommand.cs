using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Users;

/// <summary>
/// Builds <c>user update</c> (#216). It merges (docs/conventions.md 5.1): only the options given
/// change. The lifecycle changes - password, disabled, lockout - are options here, as they are on
/// <c>member update</c>, rather than verbs of their own.
/// </summary>
public static class UsersUpdateCommand
{
    /// <summary>Builds the command.</summary>
    /// <param name="executor">Runs the command against the resolved client.</param>
    /// <returns>The <c>update</c> command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "update",
            "Update a backoffice user by id, email or username.\n\n"
                + "Omitted options keep their values. --group replaces the user's groups, and "
                + "--new-password is an admin reset that needs no current password."
        )
            .WithExamples(
                "umbraco user update editor@example.com --name \"Jane Roe\" --group editor --group translator",
                "umbraco user update 3f7a8b2e-... --new-password <secret> --unlock",
                "umbraco user update editor@example.com --disabled",
                "umbraco user update editor@example.com --disabled false   # enable again"
            )
            .Mutating();
        var idArg = Reference.Argument(EntityKind.User);
        var emailOpt = new Option<string?>("--email") { Description = "New email address." };
        var nameOpt = new Option<string?>("--name") { Description = "New display name." };
        var userNameOpt = new Option<string?>("--username")
        {
            Description =
                "New login name. When omitted with --email, a login name equal to the old email follows the new one.",
        };
        var groupOpt = ListOption.Strings(
            "--group",
            "User groups, by alias, name or id. They REPLACE the user's groups - omit to leave them alone."
        );
        var cultureOpt = new Option<string?>("--culture")
        {
            Description = "The user's backoffice UI language, as an ISO code (e.g. en-US).",
        };
        var passwordOpt = new Option<string?>("--new-password")
        {
            Description = "Set a new password. No current password is required (admin reset).",
        };
        var disabledOpt = new Option<bool?>("--disabled")
        {
            Description = "Disable the user (--disabled), or enable them again (--disabled false).",
        };
        var unlockOpt = new Option<bool>("--unlock")
        {
            Description = "Clear a lockout caused by failed logins.",
        };
        cmd.Add(idArg);
        cmd.Add(emailOpt);
        cmd.Add(nameOpt);
        cmd.Add(userNameOpt);
        cmd.Add(groupOpt);
        cmd.Add(cultureOpt);
        cmd.Add(passwordOpt);
        cmd.Add(disabledOpt);
        cmd.Add(unlockOpt);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) =>
                        idArg.WithResolvedAsync(
                            parseResult,
                            client,
                            id =>
                                client
                                    .UpdateUserAsync(
                                        id,
                                        new UpdateUserRequest
                                        {
                                            Email = parseResult.GetValue(emailOpt),
                                            Name = parseResult.GetValue(nameOpt),
                                            UserName = parseResult.GetValue(userNameOpt),
                                            UserGroups = parseResult.GetValue(groupOpt),
                                            LanguageIsoCode = parseResult.GetValue(cultureOpt),
                                            NewPassword = parseResult.GetValue(passwordOpt),
                                            Disabled = parseResult.GetValue(disabledOpt),
                                            Unlock = parseResult.GetValue(unlockOpt),
                                        },
                                        c
                                    )
                                    // An update's data is the resulting user, as get shows it.
                                    .ThenRead(() => client.GetUserByIdAsync(id, c)),
                            c
                        ),
                    ct
                )
        );

        return cmd;
    }
}
