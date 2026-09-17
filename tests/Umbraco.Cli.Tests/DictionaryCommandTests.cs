using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the dictionary tree/create-with-parent/move verbs (#110): the create
/// maps an optional <c>--parent</c>, the tree threads <c>--parent</c> to the client, and move maps
/// its target. Client HTTP behaviour is covered separately by <see cref="DictionaryClientTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class DictionaryCommandTests
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
        root.Add(DictionaryCommand.Build(executor));
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
    public async Task Create_WithParent_MapsParentReference()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} dictionary create --key Nav.Home --parent {parent}");

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.DictionaryItemsCreated);
        Assert.Equal("Nav.Home", created.Name);
        Assert.Equal(parent, created.Parent!.Id);
    }

    [Fact]
    public async Task Create_WithoutParent_LeavesParentNull()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} dictionary create --key Nav.Home");

        Assert.Equal(0, exit);
        Assert.Null(Assert.Single(fake.DictionaryItemsCreated).Parent);
    }

    [Fact]
    public async Task Tree_WithParent_PassesParentIdToClient()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryTreeItems.Add(
            new DictionaryTreeItem
            {
                Id = Guid.NewGuid(),
                Name = "Home",
                HasChildren = false,
            }
        );
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} dictionary tree --parent {parent}");

        Assert.Equal(0, exit);
        Assert.Equal(parent, fake.LastDictionaryTreeArgs!.Value.ParentId);
    }

    [Fact]
    public async Task Tree_Root_PassesNullParent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} dictionary tree");

        Assert.Equal(0, exit);
        Assert.Null(fake.LastDictionaryTreeArgs!.Value.ParentId);
    }

    [Fact]
    public async Task Move_MapsIdAndTarget()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();
        var target = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} dictionary move {id} --target {target}");

        Assert.Equal(0, exit);
        var (movedId, movedTarget) = Assert.Single(fake.DictionaryItemsMoved);
        Assert.Equal(id, movedId);
        Assert.Equal(target, movedTarget);
    }
}
