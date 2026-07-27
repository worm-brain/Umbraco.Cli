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
        CommandExecutor executor
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
        cmd.Add(ProfilesCommand.Build(global.Output, global.Config, configStore));
        cmd.Add(UseProfileCommand.Build(global.Output, global.Config, configStore));
        cmd.Add(WhoAmICommand.Build(executor));
        return cmd;
    }
}
