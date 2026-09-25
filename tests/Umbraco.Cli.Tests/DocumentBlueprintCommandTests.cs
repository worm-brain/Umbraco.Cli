using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.DocumentBlueprints;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the document-blueprint noun (#113): the branches that live in the
/// command - flag-vs-json-body request building, the local <c>--schema</c> describe-and-exit, option
/// mapping (document-type alias parsing, move target), and confirmation gating on delete. Client HTTP
/// behaviour is covered separately by the client tests.
/// </summary>
[Collection("ConsoleCapture")]
public class DocumentBlueprintCommandTests
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
        root.Add(DocumentBlueprintCommand.Build(executor));
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
    public async Task Create_WithFlags_MapsDocumentTypeAliasAndVariantName()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(
            root,
            $"{Auth} document-blueprint create --document-type textPage --name Starter"
        );

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.BlueprintsCreated);
        Assert.Equal("textPage", created.DocumentType.Alias);
        Assert.Equal(Guid.Empty, created.DocumentType.Id); // alias, not a UUID
        Assert.Equal("Starter", Assert.Single(created.Variants).Name);
    }

    [Fact]
    public async Task Create_WithCulture_PutsItOnTheVariant()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        await Run(
            root,
            $"{Auth} document-blueprint create --document-type textPage --name Starter --culture da-DK"
        );

        Assert.Equal(
            "da-DK",
            Assert.Single(Assert.Single(fake.BlueprintsCreated).Variants).Culture
        );
    }

    [Fact]
    public async Task Create_WithoutCulture_LeavesItForTheClientToDefault()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        await Run(
            root,
            $"{Auth} document-blueprint create --document-type textPage --name Starter"
        );

        Assert.Null(Assert.Single(Assert.Single(fake.BlueprintsCreated).Variants).Culture);
    }

    [Fact]
    public async Task Create_WithJsonBody_DeserializesValues()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var path = Path.Combine(Path.GetTempPath(), $"bp-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(
            path,
            """{"documentType":{"alias":"textPage"},"values":[{"alias":"title","value":"Hello"}],"variants":[{"name":"Starter"}]}"""
        );

        try
        {
            var exit = await Run(root, $"{Auth} document-blueprint create --json-body {path}");

            Assert.Equal(0, exit);
            var created = Assert.Single(fake.BlueprintsCreated);
            Assert.Equal("title", Assert.Single(created.Values).Alias);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Create_Schema_PrintsAndExitsWithoutCalling()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        // No --host/--token: --schema must not need host/auth.
        var exit = await Run(root, "document-blueprint create --schema");

        Assert.Equal(0, exit);
        Assert.Empty(fake.BlueprintsCreated);
    }

    [Fact]
    public async Task Create_MissingRequiredInput_IsParseError()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var parse = root.Parse($"{Auth} document-blueprint create");

        Assert.NotEmpty(parse.Errors); // needs --document-type + --name, or --json-body/--schema
    }

    [Fact]
    public async Task FromDocument_MapsSourceAndName()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var source = Guid.NewGuid();

        var exit = await Run(
            root,
            $"{Auth} document-blueprint from-document {source} --name Starter"
        );

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.BlueprintsFromDocumentCreated);
        Assert.Equal(source, created.Document);
        Assert.Equal("Starter", created.Name);
    }

    [Fact]
    public async Task Move_WithoutTarget_MovesToRoot()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} document-blueprint move {id}");

        Assert.Equal(0, exit);
        var (movedId, target) = Assert.Single(fake.BlueprintsMoved);
        Assert.Equal(id, movedId);
        Assert.Null(target); // omitted --target -> root
    }

    [Fact]
    public async Task Delete_NonInteractiveWithoutYes_AbortsAndDoesNotDelete()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} document-blueprint delete {Guid.NewGuid()}");

        Assert.Equal(2, exit);
        Assert.Empty(fake.BlueprintsDeleted);
    }

    [Fact]
    public async Task Delete_WithYes_Deletes()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} --yes document-blueprint delete {id}");

        Assert.Equal(0, exit);
        Assert.Equal(id, Assert.Single(fake.BlueprintsDeleted));
    }

    [Fact]
    public async Task FolderCreate_MapsNameAndParent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var parent = Guid.NewGuid();

        var exit = await Run(
            root,
            $"{Auth} document-blueprint folder create --name Marketing --parent {parent}"
        );

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.BlueprintFoldersCreated);
        Assert.Equal("Marketing", created.Name);
        Assert.Equal(parent, created.Parent!.Id);
    }

    [Fact]
    public async Task FolderDelete_NonInteractiveWithoutYes_Aborts()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} document-blueprint folder delete {Guid.NewGuid()}");

        Assert.Equal(2, exit);
        Assert.Empty(fake.BlueprintFoldersDeleted);
    }
}
