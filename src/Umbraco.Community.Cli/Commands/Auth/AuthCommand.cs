using System.CommandLine;
using Umbraco.Community.Cli.Client;
using Umbraco.Community.Cli.Infrastructure;
using Umbraco.Community.Cli.Infrastructure.Config;

namespace Umbraco.Community.Cli.Commands.Auth;

/// <summary>Wires the <c>auth</c> noun: credentials, profiles and checks against an instance.</summary>
public static class AuthCommand
{
    /// <summary>
    /// Builds the <c>auth</c> noun with its verbs. Unlike the other nouns it takes the config store and
    /// token service directly: its verbs work on the credentials themselves, not through a signed-in
    /// client.
    /// </summary>
    /// <param name="global">The recursive global options.</param>
    /// <param name="configStore">The default config store.</param>
    /// <param name="authService">Exchanges client credentials for tokens.</param>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="httpClientFactory">Creates the HTTP clients <c>doctor</c> probes with.</param>
    /// <param name="clientFactory">Wraps <c>doctor</c>'s HTTP client in the management client.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(
        GlobalOptions global,
        ConfigStore configStore,
        UmbracoAuthService authService,
        CommandExecutor executor,
        IHttpClientFactory httpClientFactory,
        IUmbracoManagementClientFactory clientFactory
    )
    {
        var cmd = new Command(
            "auth",
            "Manage authentication with your Umbraco instance."
        ).WithExamples(
            "umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>",
            "umbraco auth whoami",
            "umbraco auth logout"
        );
        cmd.Add(LoginCommand.Build(global, configStore, authService));
        cmd.Add(LogoutCommand.Build(global, configStore, authService));
        // Saved credential profiles are one sub-resource: a noun, not two verbs (docs/conventions.md 1.4).
        var profile = new Command(
            "profile",
            "List saved credential profiles and choose the default."
        ).WithExamples("umbraco auth profile list", "umbraco auth profile use prod");
        profile.Add(ProfilesCommand.Build(global, configStore));
        profile.Add(UseProfileCommand.Build(global, configStore));
        cmd.Add(profile);
        cmd.Add(WhoAmICommand.Build(executor));
        cmd.Add(
            AuthDoctorCommand.Build(
                global,
                configStore,
                authService,
                httpClientFactory,
                clientFactory
            )
        );
        return cmd;
    }
}
