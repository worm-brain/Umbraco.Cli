using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The CLI's composition root: the DI container and the command tree built from it. It is the
/// one place both are wired, so <c>Program.cs</c> and the startup benchmarks
/// (<c>tests/Umbraco.Cli.Benchmarks</c>, #409) pay for exactly the same registrations rather
/// than a copy that drifts.
/// </summary>
public static class CliServices
{
    /// <summary>
    /// Registers every service the CLI resolves and builds the container. Nothing is resolved
    /// yet, and registering touches no file, network or console: the config and token-cache
    /// files are only read when a command runs.
    /// </summary>
    /// <returns>The container. The caller owns it.</returns>
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();

        // --verbose (#374): one gated logging handler on every client, switched on after parsing.
        // The default client carries the OAuth token exchange and the auth doctor probes, so they
        // are logged too (with the client secret and tokens redacted).
        services.AddSingleton<VerboseState>();
        services.AddTransient<VerboseHttpHandler>();
        services
            .AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .AddHttpMessageHandler<VerboseHttpHandler>();

        // Mutation interceptor: powers --dry-run (and, later, #69/#70). Registered as the
        // innermost handler on the Management-API client so it sees the fully-built request;
        // gated by the per-invocation MutationInterceptState so it is a no-op unless --dry-run is
        // set. Not added to the auth (default) client, so the OAuth token exchange is never
        // intercepted.
        services.AddSingleton<MutationInterceptState>();
        services.AddTransient<MutationInterceptorHandler>();

        // 401 recovery (#248): outermost, so a retried request goes through the rest of the
        // pipeline (and --verbose logs both attempts). Not on the auth (default) client: the token
        // exchange is what it calls.
        services.AddSingleton<TokenRefreshState>();
        services.AddTransient<TokenRefreshHandler>();

        // Error bodies are read as ProblemDetails (#286); one that is not JSON (a proxy's HTML
        // page) is dropped here so the failure keeps its status instead of crashing the parse.
        services.AddTransient<UnreadableErrorBodyHandler>();

        // The verbose handler sits inside the 401 retry (so both attempts are logged) and outside
        // the dry-run interceptor (so a recorded write is logged with its fake "not sent" response).
        services
            .AddHttpClient("umbraco")
            .AddHttpMessageHandler<TokenRefreshHandler>()
            .AddHttpMessageHandler<UnreadableErrorBodyHandler>()
            .AddHttpMessageHandler<VerboseHttpHandler>()
            .AddHttpMessageHandler<MutationInterceptorHandler>();
        services.AddSingleton<ConfigStore>();

        // Tokens outlive the process in a per-user file unless UMBRACO_NO_TOKEN_CACHE is set (#248).
        services.AddSingleton(sp => new UmbracoAuthService(
            sp.GetRequiredService<IHttpClientFactory>(),
            cache: FileTokenCache.FromEnvironment()
        ));
        services.AddSingleton<GlobalOptions>();
        services.AddSingleton<IUmbracoManagementClientFactory, UmbracoManagementClientFactory>();
        services.AddSingleton(sp => new CommandContextFactory(
            sp.GetRequiredService<ConfigStore>(),
            sp.GetRequiredService<UmbracoAuthService>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<GlobalOptions>(),
            sp.GetRequiredService<IUmbracoManagementClientFactory>(),
            sp.GetRequiredService<MutationInterceptState>(),
            sp.GetRequiredService<TokenRefreshState>()
        ));
        services.AddSingleton<IConfirmationPrompt, ConsoleConfirmationPrompt>();
        services.AddSingleton(sp => new CommandExecutor(
            sp.GetRequiredService<CommandContextFactory>(),
            sp.GetRequiredService<IConfirmationPrompt>()
        ));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds the full <c>umbraco</c> command tree (<see cref="CliRoot.Build"/>) from the
    /// container's singletons, resolving the executor and everything it depends on.
    /// </summary>
    /// <param name="services">A container from <see cref="CreateProvider"/>.</param>
    /// <returns>The root command, ready to parse.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="services"/> is missing one of the CLI's services, e.g. a container not
    /// built by <see cref="CreateProvider"/>.
    /// </exception>
    public static RootCommand BuildRoot(IServiceProvider services) =>
        CliRoot.Build(
            services.GetRequiredService<GlobalOptions>(),
            services.GetRequiredService<ConfigStore>(),
            services.GetRequiredService<UmbracoAuthService>(),
            services.GetRequiredService<CommandExecutor>(),
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<IUmbracoManagementClientFactory>()
        );
}
