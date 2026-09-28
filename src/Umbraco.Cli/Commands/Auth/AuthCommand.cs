using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Commands.Auth;

public static class AuthCommand
{
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
