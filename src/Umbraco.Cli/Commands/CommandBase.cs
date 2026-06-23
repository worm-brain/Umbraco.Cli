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

    public CommandContextFactory(
        ConfigStore configStore,
        UmbracoAuthService authService,
        IHttpClientFactory httpClientFactory)
    {
        _configStore = configStore;
        _authService = authService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<CommandContext> CreateAsync(
        string? hostOverride,
        string? tokenOverride,
        OutputFormat? outputFormat,
        string commandName,
        CancellationToken ct = default)
    {
        var output = OutputWriterFactory.Create(outputFormat);
        var config = _configStore.Load();

        var host = hostOverride ?? config.Host;
        if (string.IsNullOrEmpty(host))
        {
            output.WriteError(2, "No Umbraco host configured. Run 'umbraco auth login' or set UMBRACO_HOST.");
            throw new OperationCanceledException();
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
                throw new OperationCanceledException();
            }
            bearerToken = await _authService.GetTokenAsync(host, config.ClientId!, config.ClientSecret!, ct);
        }

        var http = _httpClientFactory.CreateClient("umbraco");
        http.BaseAddress = new Uri(host.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);

        return new CommandContext
        {
            Output = output,
            Client = new UmbracoManagementClient(http),
            CommandName = commandName,
        };
    }
}
