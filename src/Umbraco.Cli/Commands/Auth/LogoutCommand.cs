using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>Wires <c>auth logout</c>.</summary>
public static class LogoutCommand
{
    /// <summary>
    /// Builds <c>auth logout</c>: removes the profile's stored credentials and, when it held any,
    /// every cached access token for its host and client id (#260).
    /// </summary>
    /// <param name="outputOption">The global <c>--output</c> option.</param>
    /// <param name="configOption">The global <c>--config</c> option.</param>
    /// <param name="profileOption">The global <c>--profile</c> option.</param>
    /// <param name="configStore">The default config store.</param>
    /// <param name="authService">Holds the token caches to clear.</param>
    /// <returns>The command.</returns>
    public static Command Build(
        Option<string?> outputOption,
        Option<string?> configOption,
        Option<string?> profileOption,
        ConfigStore configStore,
        UmbracoAuthService authService
    )
    {
        var cmd = new Command(
            "logout",
            "Remove stored Umbraco credentials for a profile.\n\nThe default profile is used unless --profile is given.\n\nExamples:\n  umbraco auth logout\n  umbraco auth logout --profile prod"
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

                // Read what was saved before it goes: the cached tokens are keyed by its host and
                // client id, and a token left behind stays usable until it expires.
                var stored = store.StoredProfile(profile);
                var outcome = store.Logout(profile);
                if (
                    outcome is not ConfigStore.LogoutOutcome.NothingToRemove
                    && stored is { Host: { Length: > 0 } host, ClientId: { Length: > 0 } clientId }
                )
                    authService.ForgetCachedTokens(host, clientId);

                switch (outcome)
                {
                    case ConfigStore.LogoutOutcome.Removed:
                        writer.WriteMessage(
                            new { profile, removed = true },
                            $"Logged out. Credentials removed{where}.",
                            CommandPath.Of(parseResult)
                        );
                        break;
                    case ConfigStore.LogoutOutcome.CredentialsClearedAllowListKept:
                        writer.WriteMessage(
                            new { profile, removed = true },
                            $"Logged out. Credentials removed{where}; the command allow-list was preserved.",
                            CommandPath.Of(parseResult)
                        );
                        break;
                    default:
                        writer.WriteMessage(
                            new { profile, removed = false },
                            "No stored credentials to remove.",
                            CommandPath.Of(parseResult)
                        );
                        break;
                }
                return Task.CompletedTask;
            }
        );

        return cmd;
    }
}
