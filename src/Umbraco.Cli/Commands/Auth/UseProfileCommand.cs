using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>
/// The <c>auth profile use</c> command (#64): switches the default credential profile so subsequent
/// commands use it without <c>--profile</c>.
/// </summary>
public static class UseProfileCommand
{
    /// <summary>Builds the <c>use</c> command.</summary>
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
            "use",
            "Set the default credential profile.\n\nExamples:\n  umbraco auth profile use prod\n  umbraco auth profile use default"
        );
        var nameArg = new Argument<string>("name")
        {
            Description = "The profile to make the default.",
        };
        cmd.Add(nameArg);

        cmd.SetAction(
            (parseResult, ct) =>
            {
                var writer = OutputWriterFactory.Create(
                    OutputFormatParser.Parse(parseResult.GetValue(outputOption))
                );
                var store = ConfigStore.Resolve(parseResult.GetValue(configOption), configStore);
                var name = parseResult.GetValue(nameArg)!;

                if (store.SetDefaultProfile(name))
                {
                    writer.WriteMessage(
                        ItemRef.Of(name),
                        $"Default profile set to '{name}'.",
                        CommandPath.Of(parseResult)
                    );
                    return Task.FromResult(0);
                }

                writer.WriteError(
                    ExitCode.Failed,
                    FailureCategory.InvalidArgument,
                    $"No profile named '{name}'. See 'umbraco auth profile list'.",
                    CommandPath.Of(parseResult)
                );
                return Task.FromResult((int)ExitCode.Failed);
            }
        );

        return cmd;
    }
}
