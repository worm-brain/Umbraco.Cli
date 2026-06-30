using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Users;

public static class UsersInviteCommand
{
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command("invite", "Send an email invitation to a new back-office user.\n\nExamples:\n  umbraco users invite --email editor@example.com --name \"Jane Smith\"\n  umbraco users invite --email admin@example.com --name \"Bob\" --message \"Welcome to the team!\"");
        var emailOpt = new Option<string>("--email") { Required = true };
        var nameOpt = new Option<string>("--name") { Required = true };
        var msgOpt = new Option<string?>("--message");
        cmd.Add(emailOpt); cmd.Add(nameOpt); cmd.Add(msgOpt);
        cmd.SetAction((parseResult, ct) =>
        {
            var email = parseResult.GetValue(emailOpt)!;
            return executor.RunMessageAsync(
                parseResult, "users.invite",
                (client, c) => client.InviteUserAsync(new InviteUserRequest { Email = email, Name = parseResult.GetValue(nameOpt)!, Message = parseResult.GetValue(msgOpt) }, c),
                $"Invitation sent to {email}.",
                ct);
        });

        return cmd;
    }
}
