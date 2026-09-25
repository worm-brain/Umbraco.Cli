using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Builds the real <c>umbraco</c> command tree (via <see cref="CliRoot"/>) with inert
/// dependencies, for tests that need the shipped surface but never make a request.
/// </summary>
internal static class TestCliRoot
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>Builds the fully-assembled root command.</summary>
    /// <returns>The same tree <c>Program.cs</c> parses against.</returns>
    public static RootCommand Build()
    {
        var stub = new StubHttpClientFactory();
        // A throwaway config path so building the tree never touches the user's real config.
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"umbraco-test-root-{Guid.NewGuid()}.json")
        );
        var authService = new UmbracoAuthService(stub);
        var globalOptions = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            authService,
            stub,
            globalOptions,
            new UmbracoManagementClientFactory(),
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, new ConsoleConfirmationPrompt());

        return CliRoot.Build(
            globalOptions,
            configStore,
            authService,
            executor,
            stub,
            new UmbracoManagementClientFactory()
        );
    }
}
