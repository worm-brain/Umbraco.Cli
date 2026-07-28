using System.CommandLine;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

public static class LogoutCommand
{
    public static Command Build(
        Option<string?> outputOption,
        Option<string?> configOption,
        Option<string?> profileOption,
        ConfigStore configStore
    )
    {
        var cmd = new Command(
            "logout",
            "Remove stored Umbraco credentials for a profile (the default profile unless --profile is given).\n\nExamples:\n  umbraco auth logout\n  umbraco auth logout --profile prod"
        );

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var writer = OutputWriterFactory.Create(
                    OutputFormatParser.Parse(parseResult.GetValue(outputOption))
                );
                var store = ConfigStore.Resolve(parseResult.GetValue(configOption), configStore);
                var profile = parseResult.GetValue(profileOption);

                // Log out of the selected profile (the default when none is given); other
                // profiles are kept. A profile carrying a command allow-list keeps that
                // guardrail (only its credentials are cleared) so logout cannot silently drop it
                // (#83); otherwise the profile is removed, and the file when it was the last.
                var where = string.IsNullOrWhiteSpace(profile) ? "" : $" (profile '{profile}')";
                switch (store.Logout(profile))
                {
                    case ConfigStore.LogoutOutcome.Removed:
                        writer.WriteMessage($"Logged out. Credentials removed{where}.");
                        break;
                    case ConfigStore.LogoutOutcome.CredentialsClearedAllowListKept:
                        writer.WriteMessage(
                            $"Logged out. Credentials removed{where}; the command allow-list was preserved."
                        );
                        break;
                    default:
                        writer.WriteMessage("No stored credentials to remove.");
                        break;
                }
                return Task.CompletedTask;
            }
        );

        return cmd;
    }
}
