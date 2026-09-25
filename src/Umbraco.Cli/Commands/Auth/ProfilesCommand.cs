using System.CommandLine;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>
/// The <c>auth profiles</c> command (#64): lists the saved credential profiles and marks which
/// one is the default.
/// </summary>
public static class ProfilesCommand
{
    /// <summary>Builds the <c>profiles</c> command.</summary>
    /// <param name="outputOption">The global output-format option.</param>
    /// <param name="configOption">The global config-path option.</param>
    /// <param name="configStore">The fallback config store.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(
        Option<string?> outputOption,
        Option<string?> configOption,
        ConfigStore configStore
    )
    {
        var cmd = new Command(
            "profiles",
            "List saved credential profiles and which one is the default."
        );

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var writer = OutputWriterFactory.Create(
                    OutputFormatParser.Parse(parseResult.GetValue(outputOption))
                );
                var store = ConfigStore.Resolve(parseResult.GetValue(configOption), configStore);
                var (names, defaultProfile) = store.ListProfiles();

                writer.WriteTable(
                    ["Profile", "Default"],
                    names.Select(name =>
                        new[]
                        {
                            name,
                            string.Equals(name, defaultProfile, StringComparison.OrdinalIgnoreCase)
                                ? "*"
                                : "",
                        }
                    ),
                    CommandPath.Of(parseResult)
                );
                return Task.CompletedTask;
            }
        );

        return cmd;
    }
}
