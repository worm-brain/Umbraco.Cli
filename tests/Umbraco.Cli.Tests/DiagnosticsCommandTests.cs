using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Diagnostics;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the diagnostics nouns (#115): option mapping (log level filter,
/// manifest scope, health group name) and confirmation gating on the action verbs (models-builder
/// build, saved-search delete). Client HTTP behaviour is covered by the client tests.
/// </summary>
[Collection("ConsoleCapture")]
public class DiagnosticsCommandTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    private sealed class Prompt(bool interactive, bool answer) : IConfirmationPrompt
    {
        public bool IsInteractive => interactive;

        public bool Confirm(string message) => answer;
    }

    private static RootCommand BuildRoot(IUmbracoManagementClient client)
    {
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")),
            new UmbracoAuthService(stub),
            stub,
            global,
            new FakeClientFactory(client),
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, new Prompt(interactive: false, answer: true));
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(ServerCommand.Build(executor));
        root.Add(HealthCommand.Build(executor));
        root.Add(LogViewerCommand.Build(executor));
        root.Add(ModelsBuilderCommand.Build(executor));
        root.Add(ManifestCommand.Build(executor));
        return root;
    }

    private static async Task<int> Run(RootCommand root, string args)
    {
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            return await root.Parse(args).InvokeAsync();
        }
        finally
        {
            Console.SetOut(orig);
        }
    }

    private const string Auth = "--host https://x --token t --output json";

    [Fact]
    public async Task ServerStatus_Succeeds()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} server status");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task HealthRun_MapsGroupName()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} health run \"Data Integrity\"");

        Assert.Equal(0, exit);
        Assert.Equal("Data Integrity", Assert.Single(fake.HealthGroupsRun));
    }

    [Fact]
    public async Task LogViewerLog_MapsLevelFilter()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(
            root,
            $"{Auth} log-viewer list --level Error --level Warning --take 25"
        );

        Assert.Equal(0, exit);
        Assert.NotNull(fake.LastLogQuery);
        Assert.Equal(25, fake.LastLogQuery!.Value.Take);
        Assert.Equal([LogLevel.Error, LogLevel.Warning], fake.LastLogQuery.Value.Levels);
        Assert.True(fake.LastLogQuery.Value.Descending); // default order
    }

    [Fact]
    public async Task LogViewerLog_Ascending_FlipsOrder()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} log-viewer list --asc");

        Assert.Equal(0, exit);
        Assert.False(fake.LastLogQuery!.Value.Descending);
    }

    [Fact]
    public async Task LogViewerList_All_PinsLaterPagesToTheFirstPagesNewestEntry()
    {
        // Newest-first offsets shift while the log grows, so --all repeated rows at page
        // boundaries; every page after the first must end where the first began.
        var newest = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var fake = new FakeUmbracoManagementClient();
        fake.LogMessages.AddRange(
            Enumerable
                .Range(0, 150)
                .Select(i => new LogMessageResponse { Timestamp = newest.AddSeconds(-i) })
        );

        await Run(BuildRoot(fake), $"{Auth} log-viewer list --all");

        Assert.Equal(newest.AddMilliseconds(1), fake.LastLogQuery!.Value.End);
    }

    [Fact]
    public async Task PinnedAfterFirstPage_Ascending_NeverPins()
    {
        var sent = new List<DateTimeOffset?>();
        var call = LogViewerCommand.PinnedAfterFirstPage(
            Recording(sent, DateTimeOffset.UnixEpoch),
            endDate: null,
            descending: false
        );

        await call(new FakeUmbracoManagementClient(), 0, 100, CancellationToken.None);
        await call(new FakeUmbracoManagementClient(), 100, 100, CancellationToken.None);

        Assert.Equal([null, null], sent);
    }

    [Fact]
    public async Task PinnedAfterFirstPage_GivenEndBeforeNewest_KeepsTheGivenEnd()
    {
        var given = DateTimeOffset.UnixEpoch;
        var sent = new List<DateTimeOffset?>();
        var call = LogViewerCommand.PinnedAfterFirstPage(
            Recording(sent, given.AddDays(1)),
            endDate: given,
            descending: true
        );

        await call(new FakeUmbracoManagementClient(), 0, 100, CancellationToken.None);
        await call(new FakeUmbracoManagementClient(), 100, 100, CancellationToken.None);

        Assert.Equal([given, given], sent);
    }

    /// <summary>A log query that records the end date it is sent and returns one entry.</summary>
    /// <param name="sent">Receives each end date sent.</param>
    /// <param name="timestamp">The returned entry's timestamp.</param>
    /// <returns>The query.</returns>
    private static Func<
        IUmbracoManagementClient,
        int,
        int,
        DateTimeOffset?,
        CancellationToken,
        Task<UmbracoResponse<PagedResponse<LogMessageResponse>>>
    > Recording(List<DateTimeOffset?> sent, DateTimeOffset timestamp) =>
        (_, _, _, end, _) =>
        {
            sent.Add(end);
            return Task.FromResult(
                UmbracoResponse<PagedResponse<LogMessageResponse>>.Success(
                    new PagedResponse<LogMessageResponse>
                    {
                        Total = 1,
                        Items = [new LogMessageResponse { Timestamp = timestamp }],
                    }
                )
            );
        };

    [Fact]
    public async Task SavedSearchCreate_MapsNameAndQuery()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(
            root,
            $"{Auth} log-viewer saved-search create --name Errors --query Level=Error"
        );

        Assert.Equal(0, exit);
        var (name, query) = Assert.Single(fake.SavedSearchesCreated);
        Assert.Equal("Errors", name);
        Assert.Equal("Level=Error", query);
    }

    [Fact]
    public async Task SavedSearchDelete_NonInteractiveWithoutYes_Aborts()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} log-viewer saved-search delete Errors");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SavedSearchesDeleted);
    }

    [Fact]
    public async Task ModelsBuilderBuild_NonInteractiveWithoutYes_Runs()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} models-builder build");

        // Regenerating generated files loses nothing, so it is not gated (docs/conventions.md 5.2).
        Assert.Equal(0, exit);
        Assert.Equal(1, fake.ModelsBuiltCount);
    }

    [Fact]
    public async Task ModelsBuilderBuild_WithYes_Builds()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} --yes models-builder build");

        Assert.Equal(0, exit);
        Assert.Equal(1, fake.ModelsBuiltCount);
    }

    [Fact]
    public async Task ManifestList_MapsScope()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} manifest list --scope Public");

        Assert.Equal(0, exit);
        Assert.Equal(ManifestScope.Public, fake.LastManifestScope);
    }
}
