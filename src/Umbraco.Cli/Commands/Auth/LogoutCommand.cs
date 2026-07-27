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

                // Remove only the selected profile (the default when none is given); other
                // profiles are kept. Deleting the last profile removes the file.
                if (store.DeleteProfile(profile))
                {
                    var where = string.IsNullOrWhiteSpace(profile) ? "" : $" (profile '{profile}')";
                    writer.WriteMessage($"Logged out. Credentials removed{where}.");
                }
                else
                {
                    writer.WriteMessage("No stored credentials to remove.");
                }
                return Task.CompletedTask;
            }
        );

        return cmd;
    }
}
