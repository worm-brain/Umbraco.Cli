using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Commands.Auth;

public static class AuthCommand
{
    public static Command Build(
        Option<string?> hostOption,
        Option<string?> tokenOption,
        Option<string?> outputOption,
        ConfigStore configStore,
        UmbracoAuthService authService,
        CommandContextFactory factory)
    {
        var cmd = new Command("auth", "Manage authentication with your Umbraco instance.");
        cmd.Add(LoginCommand.Build(hostOption, outputOption, configStore, authService));
        cmd.Add(LogoutCommand.Build(outputOption, configStore));
        cmd.Add(WhoAmICommand.Build(hostOption, tokenOption, outputOption, factory));
        return cmd;
    }
}
