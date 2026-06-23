using System.CommandLine;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

public static class LogoutCommand
{
    public static Command Build(Option<string?> outputOption, ConfigStore configStore)
    {
        var cmd = new Command("logout", "Clear stored Umbraco credentials.");

        cmd.SetAction((parseResult, ct) =>
        {
            var writer = OutputWriterFactory.Create(LoginCommand.ParseOutputFormat(parseResult.GetValue(outputOption)));
            configStore.Delete();
            writer.WriteMessage("Logged out. Credentials removed.");
            return Task.CompletedTask;
        });

        return cmd;
    }
}
