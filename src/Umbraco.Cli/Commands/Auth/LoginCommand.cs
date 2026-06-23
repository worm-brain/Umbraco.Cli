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
        ConfigStore configStore,
        UmbracoAuthService authService)
    {
        var cmd = new Command("login", "Authenticate with an Umbraco instance using Client Credentials.");

        var clientIdOpt = new Option<string?>("--client-id") { Description = "API User client ID." };
        var clientSecretOpt = new Option<string?>("--client-secret") { Description = "API User client secret." };

        cmd.Add(clientIdOpt);
        cmd.Add(clientSecretOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var outputFormat = ParseOutputFormat(parseResult.GetValue(outputOption));
            var writer = OutputWriterFactory.Create(outputFormat);

            var host = parseResult.GetValue(hostOption)
                ?? (outputFormat != OutputFormat.Json
                    ? AnsiConsole.Ask<string>("Umbraco host URL (e.g. https://mysite.com):")
                    : null);

            var clientId = parseResult.GetValue(clientIdOpt)
                ?? (outputFormat != OutputFormat.Json
                    ? AnsiConsole.Ask<string>("Client ID:")
                    : null);

            var clientSecret = parseResult.GetValue(clientSecretOpt)
                ?? (outputFormat != OutputFormat.Json
                    ? AnsiConsole.Prompt(new TextPrompt<string>("Client Secret:").Secret())
                    : null);

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                writer.WriteError(3, "--host, --client-id, and --client-secret are all required.");
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

            configStore.Save(new CliConfig
            {
                Host = host,
                ClientId = clientId,
                ClientSecret = clientSecret,
            });

            writer.WriteMessage($"Logged in to {host}");
            return 0;
        });

        return cmd;
    }

    internal static OutputFormat? ParseOutputFormat(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "human" => OutputFormat.Human,
            _ => null,
        };
}
