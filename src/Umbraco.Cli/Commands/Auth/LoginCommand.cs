using System.CommandLine;
using Spectre.Console;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Auth;

/// <summary>Wires <c>auth login</c>.</summary>
public static class LoginCommand
{
    /// <summary>
    /// Builds <c>auth login</c>: checks the credentials against the host, then saves them to the
    /// profile resolved as for every other command: <c>--profile</c>, else <c>UMBRACO_PROFILE</c>,
    /// else the default (#303).
    /// </summary>
    /// <param name="global">The global options (<c>--host</c>, <c>--output</c>, <c>--config</c>, <c>--profile</c>...).</param>
    /// <param name="configStore">The default config store.</param>
    /// <param name="authService">Fetches a token to prove the credentials work.</param>
    /// <returns>The command.</returns>
    public static Command Build(
        GlobalOptions global,
        ConfigStore configStore,
        UmbracoAuthService authService
    )
    {
        var cmd = new Command(
            "login",
            "Authenticate with an Umbraco instance using Client Credentials.\nCredentials are saved to config for future calls, in the profile named by --profile, else UMBRACO_PROFILE, else the default profile."
        ).WithExamples(
            "umbraco auth login",
            "umbraco auth login --host https://mysite.com --client-id <id> --client-secret <secret>",
            "umbraco auth login --output json --host https://mysite.com --client-id <id> --client-secret <secret>"
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
                // The format decides whether to prompt for missing values (never under JSON).
                var outputFormat = global.FormatOf(parseResult);
                var writer = global.CreateWriter(parseResult);

                var host =
                    parseResult.GetValue(global.Host)
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
                    // Nothing is saved: a host that could not be reached proved nothing either.
                    return (int)
                        CommandContextFactory.WriteAuthFailure(
                            writer,
                            ex,
                            CommandPath.Of(parseResult)
                        );
                }

                var store = ConfigStore.Resolve(global.ConfigPath(parseResult), configStore);
                // Resolved once, so the save and the response name the same profile (#303).
                var profile = store.ResolveProfileName(parseResult.GetValue(global.Profile));
                // Save preserves the profile's existing allow-list (#69), so no manual merge is
                // needed here.
                store.Save(
                    new CliConfig
                    {
                        Host = host,
                        ClientId = clientId,
                        ClientSecret = clientSecret,
                    },
                    profile
                );

                // Every success has data (docs/conventions.md 6.2): where the credentials went.
                writer.WriteMessage(
                    new { host, profile },
                    $"Logged in to {host} (profile '{profile}').",
                    CommandPath.Of(parseResult)
                );
                return 0;
            }
        );

        return cmd;
    }
}
