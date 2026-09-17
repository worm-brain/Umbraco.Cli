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
/// Command-layer behaviour of the content/media <c>sort</c> verbs (#88): the ordered
/// <c>--children</c> list and optional <c>--parent</c> are threaded through to the client in the
/// order the user typed them. Client HTTP behaviour is covered separately by <see cref="SortClientTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class SortCommandTests
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
    public async Task ContentSort_WithParent_PassesOrderedChildrenInGivenOrder()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} content sort --parent {parent} --children {a} {b} {c}");

        Assert.Equal(0, exit);
        var (parentId, children) = Assert.Single(fake.ContentSorted);
        Assert.Equal(parent, parentId);
        Assert.Equal(new[] { a, b, c }, children); // order preserved end-to-end
    }

    [Fact]
    public async Task ContentSort_WithoutParent_PassesNullParent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} content sort --children {a} {b}");

        Assert.Equal(0, exit);
        var (parentId, children) = Assert.Single(fake.ContentSorted);
        Assert.Null(parentId);
        Assert.Equal(new[] { a, b }, children);
    }

    [Fact]
    public async Task MediaSort_WithParent_PassesOrderedChildren()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} media sort --parent {parent} --children {a} {b}");

        Assert.Equal(0, exit);
        var (parentId, children) = Assert.Single(fake.MediaSorted);
        Assert.Equal(parent, parentId);
        Assert.Equal(new[] { a, b }, children);
    }
}
