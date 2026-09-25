using System.CommandLine;
using Spectre.Console;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

public static class LoginCommand
{
    public static Command Build(
        Option<string?> hostOption,
        Option<string?> outputOption,
        Option<string?> configOption,
        Option<string?> profileOption,
        ConfigStore configStore,
        UmbracoAuthService authService
    )
    {
        var cmd = new Command(
            "login",
            "Authenticate with an Umbraco instance using Client Credentials.\nCredentials are saved to config for future calls.\n\nExamples:\n  umbraco auth login\n  umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>\n  umbraco auth login --output json --host https://mysite.com --client-id <id> --client-secret <secret>"
        );

        var clientIdOpt = new Option<string?>("--client-id")
        {
            Description = "OAuth2 Client ID from the Umbraco API Users section.",
        };
        var clientSecretOpt = new Option<string?>("--client-secret")
        {
            Description = "OAuth2 Client Secret from the Umbraco API Users section.",
        };

        cmd.Add(clientIdOpt);
        cmd.Add(clientSecretOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var outputFormat = OutputFormatParser.Parse(parseResult.GetValue(outputOption));
                var writer = OutputWriterFactory.Create(outputFormat);

                var host =
                    parseResult.GetValue(hostOption)
                    ?? (
                        outputFormat != OutputFormat.Json
                            ? AnsiConsole.Ask<string>("Umbraco host URL (e.g. https://mysite.com):")
                            : null
                    );

                var clientId =
                    parseResult.GetValue(clientIdOpt)
                    ?? (
                        outputFormat != OutputFormat.Json
                            ? AnsiConsole.Ask<string>("Client ID:")
                            : null
                    );

                var clientSecret =
                    parseResult.GetValue(clientSecretOpt)
                    ?? (
                        outputFormat != OutputFormat.Json
                            ? AnsiConsole.Prompt(new TextPrompt<string>("Client Secret:").Secret())
                            : null
                    );

                if (
                    string.IsNullOrEmpty(host)
                    || string.IsNullOrEmpty(clientId)
                    || string.IsNullOrEmpty(clientSecret)
                )
                {
                    // Missing input is the caller's to fix, like any other invalid argument.
                    writer.WriteError(
                        ExitCode.Failed,
                        FailureCategory.InvalidArgument,
                        "--host, --client-id, and --client-secret are all required.",
                        CommandPath.Of(parseResult)
                    );
                    return (int)ExitCode.Failed;
                }

                try
                {
                    // Fresh: login exists to prove these credentials work, not that a token is cached.
                    await authService.GetTokenAsync(host, clientId, clientSecret, ct, fresh: true);
                }
                catch (UmbracoAuthException ex)
                {
                    writer.WriteError(
                        ExitCode.Aborted,
                        FailureCategory.NotAuthenticated,
                        $"Authentication failed: {ex.Message}",
                        CommandPath.Of(parseResult)
                    );
                    return (int)ExitCode.Aborted;
                }

                var store = ConfigStore.Resolve(parseResult.GetValue(configOption), configStore);
                var profile = parseResult.GetValue(profileOption);
                // Save saves to the named profile (or the current default) and preserves that
                // profile's existing allow-list (#69), so no manual merge is needed here.
                store.Save(
                    new CliConfig
                    {
                        Host = host,
                        ClientId = clientId,
                        ClientSecret = clientSecret,
                    },
                    profile
                );

                var where = string.IsNullOrWhiteSpace(profile) ? "" : $" (profile '{profile}')";
                // Every success has data (docs/conventions.md 6.2): where the credentials went.
                writer.WriteMessage(
                    new { host, profile },
                    $"Logged in to {host}{where}",
                    CommandPath.Of(parseResult)
                );
                return 0;
            }
        );

        return cmd;
    }
}
