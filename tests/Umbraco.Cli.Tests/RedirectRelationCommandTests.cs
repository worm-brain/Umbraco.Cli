using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Redirects;
using Umbraco.Cli.Commands.Relations;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the redirect and relation nouns (#118): option mapping (redirect
/// list --content routing, relation --type) and confirmation gating on the redirect writes (delete,
/// tracking toggle). Client HTTP behaviour is covered by the client tests.
/// </summary>
[Collection("ConsoleCapture")]
public class RedirectRelationCommandTests
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
        root.Add(RedirectCommand.Build(executor));
        root.Add(RelationTypeCommand.Build(executor));
        root.Add(RelationCommand.Build(executor));
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
    public async Task RedirectList_WithFilter_UsesGlobalList()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} redirect list --filter old-page");

        Assert.Equal(0, exit);
        Assert.Equal("old-page", fake.LastRedirectFilter);
        Assert.Null(fake.LastRedirectContentKey);
    }

    [Fact]
    public async Task RedirectList_WithContent_RoutesToForContent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var key = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} redirect list --content {key}");

        Assert.Equal(0, exit);
        Assert.Equal(key, fake.LastRedirectContentKey);
    }

    [Fact]
    public async Task RedirectDelete_NonInteractiveWithoutYes_Aborts()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} redirect delete {Guid.NewGuid()}");

        Assert.Equal(2, exit);
        Assert.Empty(fake.RedirectsDeleted);
    }

    [Fact]
    public async Task RedirectTrackingDisable_WithYes_SetsFalse()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} --yes redirect tracking disable");

        Assert.Equal(0, exit);
        Assert.Equal(false, Assert.Single(fake.RedirectTrackingSet));
    }

    [Fact]
    public async Task RedirectTrackingEnable_NonInteractiveWithoutYes_Runs()
    {
        // #249: enabling turns a protection on, so it needs no --yes.
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} redirect tracking enable");

        Assert.Equal(0, exit);
        Assert.Single(fake.RedirectTrackingSet);
    }

    [Fact]
    public async Task RedirectTrackingDisable_NonInteractiveWithoutYes_Aborts()
    {
        // Disabling stops redirects being recorded site-wide, so it stays confirmation-gated.
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} redirect tracking disable");

        Assert.Equal(2, exit);
        Assert.Empty(fake.RedirectTrackingSet);
    }

    [Fact]
    public async Task RelationTypeGet_MapsId()
    {
        var fake = new FakeUmbracoManagementClient();
        var id = Guid.NewGuid();
        fake.RelationTypes.Add(
            new RelationTypeResponse
            {
                Id = id,
                Alias = "a",
                Name = "A",
            }
        );
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} relation-type get {id}");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RelationList_MapsRelationTypeId()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var typeId = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} relation list --type {typeId}");

        Assert.Equal(0, exit);
        Assert.Equal(typeId, fake.LastRelationTypeQueried);
    }

    [Fact]
    public void RelationList_MissingType_IsParseError()
    {
        var root = BuildRoot(new FakeUmbracoManagementClient());

        var parse = root.Parse($"{Auth} relation list");

        Assert.NotEmpty(parse.Errors); // --type is required
    }
}
