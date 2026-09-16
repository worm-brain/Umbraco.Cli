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
            $"{Auth} log-viewer log --level Error --level Warning --take 25"
        );

        Assert.Equal(0, exit);
        Assert.NotNull(fake.LastLogQuery);
        Assert.Equal(25, fake.LastLogQuery!.Value.Take);
        Assert.Equal(["Error", "Warning"], fake.LastLogQuery.Value.Levels);
        Assert.True(fake.LastLogQuery.Value.Descending); // default order
    }

    [Fact]
    public async Task LogViewerLog_Ascending_FlipsOrder()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} log-viewer log --ascending");

        Assert.Equal(0, exit);
        Assert.False(fake.LastLogQuery!.Value.Descending);
    }

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
    public async Task ModelsBuilderBuild_NonInteractiveWithoutYes_Aborts()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} models-builder build");

        Assert.Equal(2, exit); // confirmation required (writes files), non-interactive
        Assert.Equal(0, fake.ModelsBuiltCount);
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
