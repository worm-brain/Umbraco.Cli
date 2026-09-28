using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>Wires <c>auth logout</c>.</summary>
public static class LogoutCommand
{
    /// <summary>
    /// Builds <c>auth logout</c>: removes the profile's stored credentials and, when it held any,
    /// every cached access token for its host and client id (#260). The profile is resolved as for
    /// every other command: <c>--profile</c>, else <c>UMBRACO_PROFILE</c>, else the default (#301).
    /// </summary>
    /// <param name="global">The global options (<c>--output</c>, <c>--config</c>, <c>--profile</c>...).</param>
    /// <param name="configStore">The default config store.</param>
    /// <param name="authService">Holds the token caches to clear.</param>
    /// <returns>The command.</returns>
    public static Command Build(
        GlobalOptions global,
        ConfigStore configStore,
        UmbracoAuthService authService
    )
    {
        var cmd = new Command(
            "logout",
            "Remove stored Umbraco credentials for a profile.\n\n"
                + "The profile is --profile, else UMBRACO_PROFILE, else the default profile. "
                + "Logging out of the default profile leaves no default: choose one with 'auth profile use'."
        ).WithExamples("umbraco auth logout", "umbraco auth logout --profile prod");

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var writer = global.CreateWriter(parseResult);
                var store = ConfigStore.Resolve(parseResult.GetValue(global.Config), configStore);

                // Log out of the resolved profile; other profiles are kept. A profile carrying a
                // command allow-list keeps that guardrail (only its credentials are cleared) so
                // logout cannot silently drop it (#83); otherwise the profile is removed, and the
                // file when it was the last.
                var (outcome, removed, profile) = store.Logout(
                    parseResult.GetValue(global.Profile)
                );

                // The removed profile is what was saved (no UMBRACO_* overrides). Its cached tokens
                // are keyed by its host and client id, and one left behind stays usable until it
                // expires.
                if (removed is { Host: { Length: > 0 } host, ClientId: { Length: > 0 } clientId })
                    authService.ForgetCachedTokens(host, clientId);

                // Removing the default while others remain leaves no default (#304); say so in
                // both outputs, so nobody assumes another profile took over.
                var defaultCleared =
                    outcome == ConfigStore.LogoutOutcome.Removed && store.DefaultProfileMissing;
                var noDefault = defaultCleared
                    ? " No profile is the default now; choose one with 'umbraco auth profile use <name>'."
                    : "";

                switch (outcome)
                {
                    case ConfigStore.LogoutOutcome.Removed:
                        writer.WriteMessage(
                            new
                            {
                                profile,
                                removed = true,
                                defaultCleared,
                            },
                            $"Logged out of profile '{profile}'. Credentials removed.{noDefault}",
                            CommandPath.Of(parseResult)
                        );
                        break;
                    case ConfigStore.LogoutOutcome.CredentialsClearedAllowListKept:
                        writer.WriteMessage(
                            new
                            {
                                profile,
                                removed = true,
                                defaultCleared,
                            },
                            $"Logged out of profile '{profile}'. Credentials removed; the command allow-list was preserved.",
                            CommandPath.Of(parseResult)
                        );
                        break;
                    default:
                        writer.WriteMessage(
                            new
                            {
                                profile,
                                removed = false,
                                defaultCleared,
                            },
                            $"No stored credentials to remove for profile '{profile}'.",
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
