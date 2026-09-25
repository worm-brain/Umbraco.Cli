using System.CommandLine;
using Umbraco.Cli.Client;
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
            "Manage authentication with your Umbraco instance.\n\nExamples:\n  umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>\n  umbraco auth whoami\n  umbraco auth logout"
        );
        cmd.Add(
            LoginCommand.Build(
                global.Host,
                global.Output,
                global.Config,
                global.Profile,
                configStore,
                authService
            )
        );
        cmd.Add(LogoutCommand.Build(global.Output, global.Config, global.Profile, configStore));
        // Saved credential profiles are one sub-resource: a noun, not two verbs (docs/conventions.md 1.4).
        var profile = new Command(
            "profile",
            "List saved credential profiles and choose the default.\n\nExamples:\n  umbraco auth profile list\n  umbraco auth profile use prod"
        );
        profile.Add(ProfilesCommand.Build(global.Output, global.Config, configStore));
        profile.Add(UseProfileCommand.Build(global.Output, global.Config, configStore));
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
