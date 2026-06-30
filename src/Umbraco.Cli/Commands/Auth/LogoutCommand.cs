using System.CommandLine;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

public static class LogoutCommand
{
    public static Command Build(
        Option<string?> outputOption,
        Option<string?> configOption,
        ConfigStore configStore
    )
    {
        var cmd = new Command(
            "logout",
            "Clear stored Umbraco credentials from the local config file.\n\nExample:\n  umbraco auth logout"
        );

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var writer = OutputWriterFactory.Create(
                    OutputFormatParser.Parse(parseResult.GetValue(outputOption))
                );
                ConfigStore.Resolve(parseResult.GetValue(configOption), configStore).Delete();
                writer.WriteMessage("Logged out. Credentials removed.");
                return Task.CompletedTask;
            }
        );

        return cmd;
    }
}
