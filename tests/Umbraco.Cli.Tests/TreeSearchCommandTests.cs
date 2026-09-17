using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the content/media <c>tree</c> and <c>find</c> verbs (#89): --depth vs
/// --recursive resolve to the right max depth, --parent is threaded through, and find dispatches to
/// name- vs path-search based on the option supplied. Client behaviour is covered by
/// <see cref="TreeSearchClientTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class TreeSearchCommandTests
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

    private sealed class Prompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => true;
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
        var executor = new CommandExecutor(factory, new Prompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(ContentCommand.Build(executor));
        root.Add(MediaCommand.Build(executor));
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
    public async Task ContentTree_Default_WalksOneLevelFromRoot()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} content tree");

        Assert.Equal(0, exit);
        Assert.Equal(1, fake.LastContentTreeArgs!.Value.MaxDepth);
        Assert.Null(fake.LastContentTreeArgs!.Value.ParentId);
    }

    [Fact]
    public async Task ContentTree_Recursive_RequestsUnboundedDepth()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} content tree --recursive");

        Assert.Equal(0, exit);
        // The client clamps to its safety cap; the command asks for "all levels".
        Assert.Equal(int.MaxValue, fake.LastContentTreeArgs!.Value.MaxDepth);
    }

    [Fact]
    public async Task ContentTree_ExplicitDepthAndParent_AreThreaded()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} content tree --parent {parent} --depth 3");

        Assert.Equal(0, exit);
        Assert.Equal(3, fake.LastContentTreeArgs!.Value.MaxDepth);
        Assert.Equal(parent, fake.LastContentTreeArgs!.Value.ParentId);
    }

    [Fact]
    public async Task ContentFind_ByName_ScopesToParent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} content find --name About --parent {parent}");

        Assert.Equal(0, exit);
        Assert.Equal("About", fake.LastContentFindByName!.Value.Query);
        Assert.Equal(parent, fake.LastContentFindByName!.Value.ParentId);
        Assert.Null(fake.LastContentFindByPath); // name mode, not path
    }

    [Fact]
    public async Task ContentFind_ByPath_DispatchesToPathSearch()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} content find --path Home/About");

        Assert.Equal(0, exit);
        Assert.Equal("Home/About", fake.LastContentFindByPath);
        Assert.Null(fake.LastContentFindByName); // path mode, not name
    }

    [Fact]
    public async Task ContentFind_NeitherOption_IsError()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} content find");

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task MediaTree_ExplicitDepth_IsThreaded()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} media tree --depth 2");

        Assert.Equal(0, exit);
        Assert.Equal(2, fake.LastMediaTreeArgs!.Value.MaxDepth);
    }

    [Fact]
    public async Task MediaFind_ByName_IsThreaded()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} media find --name logo");

        Assert.Equal(0, exit);
        Assert.Equal("logo", fake.LastMediaFindByName!.Value.Query);
    }

    [Fact]
    public async Task MediaFind_ByPath_IsThreaded()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} media find --path Images/Logos");

        Assert.Equal(0, exit);
        Assert.Equal("Images/Logos", fake.LastMediaFindByPath);
    }
}
