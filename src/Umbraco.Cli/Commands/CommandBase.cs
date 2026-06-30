using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

public sealed class CommandContext
{
    public required IOutputWriter Output { get; init; }
    public required IUmbracoManagementClient Client { get; init; }
    public required string CommandName { get; init; }
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}

public sealed class CommandContextFactory
{
    private readonly ConfigStore _configStore;
    private readonly UmbracoAuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GlobalOptions _globalOptions;
    private readonly IUmbracoManagementClientFactory _clientFactory;

    public CommandContextFactory(
        ConfigStore configStore,
        UmbracoAuthService authService,
        IHttpClientFactory httpClientFactory,
        GlobalOptions globalOptions,
        IUmbracoManagementClientFactory clientFactory)
    {
        _configStore = configStore;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _globalOptions = globalOptions;
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Builds the context for a command from the parsed global options. Throws
    /// <see cref="CommandAbortedException"/> (after writing the error) when no host is
    /// configured or the caller is not authenticated.
    /// </summary>
    public async Task<CommandContext> CreateAsync(
        ParseResult parseResult,
        string commandName,
        CancellationToken ct = default)
    {
        var hostOverride = parseResult.GetValue(_globalOptions.Host);
        var tokenOverride = parseResult.GetValue(_globalOptions.Token);
        var outputFormat = OutputFormatParser.Parse(parseResult.GetValue(_globalOptions.Output));

        var output = OutputWriterFactory.Create(outputFormat);
        var config = _configStore.Load();

        var host = hostOverride ?? config.Host;
        if (string.IsNullOrEmpty(host))
        {
            output.WriteError(2, "No Umbraco host configured. Run 'umbraco auth login' or set UMBRACO_HOST.");
            throw new CommandAbortedException();
        }

        string bearerToken;
        if (!string.IsNullOrEmpty(tokenOverride))
        {
            bearerToken = tokenOverride;
        }
        else
        {
            if (!config.IsComplete)
            {
                output.WriteError(2, "Not authenticated. Run 'umbraco auth login' or set UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET.");
                throw new CommandAbortedException();
            }
            try
            {
                bearerToken = await _authService.GetTokenAsync(host, config.ClientId!, config.ClientSecret!, ct);
            }
            catch (UmbracoAuthException ex)
            {
                output.WriteError(2, $"Authentication failed: {ex.Message}");
                throw new CommandAbortedException();
            }
        }

        var http = _httpClientFactory.CreateClient("umbraco");
        http.BaseAddress = new Uri(host.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);

        return new CommandContext
        {
            Output = output,
            Client = _clientFactory.Create(http),
            CommandName = commandName,
        };
    }
}
