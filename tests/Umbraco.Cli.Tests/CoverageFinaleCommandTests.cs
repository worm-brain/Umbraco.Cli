using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Examine;
using Umbraco.Cli.Commands.Imaging;
using Umbraco.Cli.Commands.PropertyTypes;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the coverage-finale nouns (#121): option mapping (searcher query,
/// imaging ids, data-type copy/move target, property-type params) and confirmation gating on the
/// action verbs (indexer rebuild, data-type copy/move, folder delete). Client HTTP behaviour is
/// covered by the client tests.
/// </summary>
[Collection("ConsoleCapture")]
public class CoverageFinaleCommandTests
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
        root.Add(IndexerCommand.Build(executor));
        root.Add(SearcherCommand.Build(executor));
        root.Add(ImagingCommand.Build(executor));
        root.Add(PropertyTypeCommand.Build(executor));
        root.Add(DataTypesCommand.Build(executor));
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
    public async Task IndexerRebuild_NonInteractiveWithoutYes_Aborts()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} indexer rebuild ExternalIndex");

        Assert.Equal(2, exit);
        Assert.Empty(fake.IndexesRebuilt);
    }

    [Fact]
    public async Task IndexerRebuild_WithYes_Rebuilds()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} --yes indexer rebuild ExternalIndex");

        Assert.Equal(0, exit);
        Assert.Equal("ExternalIndex", Assert.Single(fake.IndexesRebuilt));
    }

    [Fact]
    public async Task SearcherQuery_MapsNameAndTerm()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} searcher query ExternalSearcher --term news");

        Assert.Equal(0, exit);
        Assert.Equal(("ExternalSearcher", "news"), fake.LastSearcherQuery);
    }

    [Fact]
    public async Task ImagingResizeUrls_MapsIdsAndWidth()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} imaging resize-urls {id} --width 300 --mode Crop");

        Assert.Equal(0, exit);
        Assert.NotNull(fake.LastResizeUrls);
        Assert.Equal([id], fake.LastResizeUrls!.Value.Ids);
        Assert.Equal(300, fake.LastResizeUrls.Value.Width);
        Assert.Equal(ImageResizeMode.Crop, fake.LastResizeUrls.Value.Mode);
    }

    [Fact]
    public async Task DataTypesMove_MapsTarget()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} --yes data-type move {id} --target {target}");

        Assert.Equal(0, exit);
        var (movedId, movedTarget) = Assert.Single(fake.DataTypesMoved);
        Assert.Equal(id, movedId);
        Assert.Equal(target, movedTarget);
    }

    [Fact]
    public async Task DataTypesCopy_NonInteractiveWithoutYes_Runs()
    {
        // #247: a copy removes nothing, so it is not destructive and needs no --yes.
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} data-type copy {Guid.NewGuid()}");

        Assert.Equal(0, exit);
        Assert.Single(fake.DataTypesCopied);
    }

    [Fact]
    public async Task DataTypesFolderCreate_MapsNameAndParent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(
            root,
            $"{Auth} data-type folder create --name Pickers --parent {parent}"
        );

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.DataTypeFoldersCreated);
        Assert.Equal("Pickers", created.Name);
        Assert.Equal(parent, created.ParentId);
    }

    [Fact]
    public async Task DataTypesFolderDelete_NonInteractiveWithoutYes_Aborts()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} data-type folder delete {Guid.NewGuid()}");

        Assert.Equal(2, exit);
        Assert.Empty(fake.DataTypeFoldersDeleted);
    }

    [Fact]
    public async Task PropertyTypeIsUsed_MapsContentTypeAndAlias()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var ctId = Guid.NewGuid();

        var exit = await Run(
            root,
            $"{Auth} property-type is-used --document-type {ctId} --alias bodyText"
        );

        Assert.Equal(0, exit);
        Assert.Equal((ctId, "bodyText"), fake.LastPropertyTypeUsedQuery);
    }
}
