using System.CommandLine;
using Spectre.Console;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

public static class LoginCommand
{
    public static Command Build(
        Option<string?> hostOption,
        Option<string?> outputOption,
        Option<string?> configOption,
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
                    writer.WriteError(
                        2,
                        "--host, --client-id, and --client-secret are all required."
                    );
                    return 2;
                }

                try
                {
                    await authService.GetTokenAsync(host, clientId, clientSecret, ct);
                }
                catch (UmbracoAuthException ex)
                {
                    writer.WriteError(2, $"Authentication failed: {ex.Message}");
                    return 2;
                }

                var store = ConfigStore.Resolve(parseResult.GetValue(configOption), configStore);
                store.Save(
                    new CliConfig
                    {
                        Host = host,
                        ClientId = clientId,
                        ClientSecret = clientSecret,
                    }
                );

                writer.WriteMessage($"Logged in to {host}");
                return 0;
            }
        );

        return cmd;
    }
}
