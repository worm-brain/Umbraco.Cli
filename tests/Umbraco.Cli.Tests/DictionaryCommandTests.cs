using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the dictionary list/tree/create-with-parent/move verbs (#110): the create
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
    public async Task List_WithParent_PassesParentIdToClient()
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

        var exit = await Run(root, $"{Auth} dictionary list --parent {parent}");

        Assert.Equal(0, exit);
        Assert.Equal(parent, fake.LastDictionaryTreeArgs!.Value.ParentId);
    }

    [Fact]
    public async Task List_Root_PassesNullParent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} dictionary list");

        Assert.Equal(0, exit);
        Assert.Null(fake.LastDictionaryTreeArgs!.Value.ParentId);
    }

    [Theory]
    [InlineData("dictionary tree", 1)]
    [InlineData("dictionary tree --recursive", int.MaxValue)]
    [InlineData("dictionary tree --recursive --depth 3", 3)]
    public async Task Tree_WalksToTheRequestedDepth(string command, int expectedDepth)
    {
        // Like content tree: one level by default, the whole subtree with --recursive, and
        // --depth wins over --recursive.
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} {command}");

        Assert.Equal(0, exit);
        Assert.Equal(expectedDepth, fake.LastDictionaryWalk!.Value.MaxDepth);
    }

    [Fact]
    public async Task Tree_Parent_WalksBeneathIt()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} dictionary tree --parent {parent}");

        Assert.Equal(0, exit);
        Assert.Equal(parent, fake.LastDictionaryWalk!.Value.Parent);
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

    /// <summary>A key is resolved in the command, then read by id (#262).</summary>
    [Fact]
    public async Task Get_ByKey_ResolvesThenReadsById()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var id = Guid.NewGuid();
        fake.References[(EntityKind.DictionaryItem, "Common.Search")] = id;
        fake.DictionaryItemsById[id] = new DictionaryItemResponse
        {
            Id = id,
            Name = "Common.Search",
        };

        // Act
        var exit = await Run(BuildRoot(fake), $"{Auth} dictionary get Common.Search");

        // Assert
        Assert.Equal(0, exit);
    }

    /// <summary>A key that resolves to nothing fails without a read.</summary>
    [Fact]
    public async Task Get_UnknownKey_FailsWithExitOne()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();

        // Act
        var exit = await Run(BuildRoot(fake), $"{Auth} dictionary get Nope");

        // Assert
        Assert.Equal(1, exit);
    }
}
