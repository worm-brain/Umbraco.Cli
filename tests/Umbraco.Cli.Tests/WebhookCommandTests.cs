using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Webhooks;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of <c>webhook get/update/delete/create</c> and <c>webhook log list</c>
/// (#237): webhooks named by name, option mapping onto the client request, and the <c>--type</c>
/// lookup across document, media and member types. What goes on the wire is covered by
/// <see cref="WebhookWireTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class WebhookCommandTests
{
    private static readonly Guid HookId = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    private sealed class NonInteractivePrompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => false;
    }

    /// <summary>A root with just the <c>webhook</c> noun, bound to <paramref name="client"/>.</summary>
    /// <param name="client">The fake client.</param>
    /// <returns>The root command.</returns>
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
        var executor = new CommandExecutor(factory, new NonInteractivePrompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(WebhooksCommand.Build(executor));
        return root;
    }

    /// <summary>Runs a command line and returns its exit code; stdout/stderr are swallowed.</summary>
    /// <param name="client">The fake client.</param>
    /// <param name="args">The command line after the connection options.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> Run(IUmbracoManagementClient client, string args)
    {
        var (out_, err) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        try
        {
            return await BuildRoot(client)
                .Parse($"--host https://x --token t --output json {args}")
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }
    }

    /// <summary>A fake that knows one webhook, named <c>Deploy</c>.</summary>
    /// <returns>The fake.</returns>
    private static FakeUmbracoManagementClient WithHook()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.Webhook, "Deploy")] = HookId;
        fake.WebhooksById[HookId] = new WebhookResponse { Id = HookId, Name = "Deploy" };
        return fake;
    }

    [Fact]
    public async Task WebhookGet_ByName_ReadsTheResolvedWebhook()
    {
        var exit = await Run(WithHook(), "webhook get Deploy");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task WebhookGet_NoHeaders_WritesAnEmptyHeadersObject()
    {
        // A webhook without headers left the key out entirely, while contentTypeKeys showed [].
        var (out_, err) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(new StringWriter());
        try
        {
            await BuildRoot(WithHook())
                .Parse("--host https://x --token t --output json webhook get Deploy")
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }

        var headers = System
            .Text.Json.JsonDocument.Parse(stdout.ToString())
            .RootElement.GetProperty("data")
            .GetProperty("headers");
        Assert.Equal("{}", headers.GetRawText().Replace(" ", ""));
    }

    [Fact]
    public async Task WebhookGet_UnknownName_Fails()
    {
        var exit = await Run(WithHook(), "webhook get Nope");

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task WebhookUpdate_EnabledFalse_SendsOnlyThat()
    {
        var fake = WithHook();

        await Run(fake, "webhook update Deploy --enabled false");

        var (id, request) = Assert.Single(fake.WebhooksUpdated);
        Assert.Equal(
            (HookId, (bool?)false, (string?)null, (IReadOnlyList<string>?)null, 0),
            (id, request.Enabled, request.Url, request.Events, request.Headers!.Count)
        );
    }

    [Fact]
    public async Task WebhookUpdate_BareEnabled_Enables()
    {
        var fake = WithHook();

        await Run(fake, "webhook update Deploy --enabled");

        Assert.True(Assert.Single(fake.WebhooksUpdated).Request.Enabled);
    }

    [Fact]
    public async Task WebhookUpdate_RepeatedHeaderName_SendsTheLastValue()
    {
        var fake = WithHook();

        await Run(fake, "webhook update Deploy --header X-Key=a --header x-key=b=c");

        var headers = Assert.Single(fake.WebhooksUpdated).Request.Headers!;
        Assert.Equal("b=c", Assert.Single(headers).Value);
    }

    [Fact]
    public async Task WebhookUpdate_HeaderWithoutEquals_IsAParseError()
    {
        var fake = WithHook();

        var exit = await Run(fake, "webhook update Deploy --header X-Key");

        Assert.Equal((1, 0), (exit, fake.WebhooksUpdated.Count));
    }

    [Fact]
    public async Task WebhookUpdate_EventsGiven_ReplacesThem()
    {
        var fake = WithHook();

        await Run(
            fake,
            "webhook update Deploy --event Umbraco.ContentPublish,Umbraco.ContentUnpublish"
        );

        Assert.Equal(
            ["Umbraco.ContentPublish", "Umbraco.ContentUnpublish"],
            Assert.Single(fake.WebhooksUpdated).Request.Events!
        );
    }

    [Fact]
    public async Task WebhookUpdate_TypeAlias_SendsTheResolvedDocumentType()
    {
        var blogPost = Guid.NewGuid();
        var fake = WithHook();
        fake.References[(EntityKind.DocumentType, "blogPost")] = blogPost;

        await Run(fake, "webhook update Deploy --type blogPost");

        Assert.Equal([blogPost], Assert.Single(fake.WebhooksUpdated).Request.ContentTypeKeys!);
    }

    [Fact]
    public async Task WebhookUpdate_TypeAliasOfAMediaType_SendsTheResolvedMediaType()
    {
        var image = Guid.NewGuid();
        var fake = WithHook();
        fake.References[(EntityKind.MediaType, "Image")] = image;

        await Run(fake, "webhook update Deploy --type Image");

        Assert.Equal([image], Assert.Single(fake.WebhooksUpdated).Request.ContentTypeKeys!);
    }

    [Fact]
    public async Task WebhookUpdate_UnknownType_UpdatesNothing()
    {
        var fake = WithHook();

        var exit = await Run(fake, "webhook update Deploy --type nope");

        Assert.Equal((1, 0), (exit, fake.WebhooksUpdated.Count));
    }

    [Fact]
    public async Task WebhookUpdate_NoTypeGiven_KeepsTheTypeFilter()
    {
        var fake = WithHook();

        await Run(fake, "webhook update Deploy --name Renamed");

        Assert.Null(Assert.Single(fake.WebhooksUpdated).Request.ContentTypeKeys);
    }

    [Fact]
    public async Task WebhookUpdate_ReplaceWithYes_SendsReplace()
    {
        var fake = WithHook();

        await Run(fake, "--yes webhook update Deploy --replace --header X-Key=a");

        Assert.True(Assert.Single(fake.WebhooksUpdated).Request.Replace);
    }

    [Fact]
    public async Task WebhookUpdate_ReplaceWithoutYes_IsRefusedNonInteractively()
    {
        // --replace can drop headers and the type filter, which the CLI cannot restore.
        var fake = WithHook();

        var exit = await Run(fake, "webhook update Deploy --replace");

        Assert.Equal((2, 0), (exit, fake.WebhooksUpdated.Count));
    }

    [Fact]
    public async Task WebhookUpdate_NoReplace_Merges()
    {
        var fake = WithHook();

        await Run(fake, "webhook update Deploy --header X-Key=a");

        Assert.False(Assert.Single(fake.WebhooksUpdated).Request.Replace);
    }

    [Fact]
    public async Task WebhookCreate_TypeAndHeader_SendsThem()
    {
        var blogPost = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DocumentType, "blogPost")] = blogPost;

        await Run(
            fake,
            "webhook create --url https://my.app/hook --event Umbraco.ContentPublish --type blogPost --header X-Api-Key=abc"
        );

        var request = Assert.Single(fake.WebhooksCreated);
        Assert.Equal(
            (blogPost, "abc"),
            (Assert.Single(request.ContentTypeKeys), request.Headers["X-Api-Key"])
        );
    }

    [Fact]
    public async Task WebhookCreate_UnknownType_CreatesNothing()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(
            fake,
            "webhook create --url https://my.app/hook --event Umbraco.ContentPublish --type nope"
        );

        Assert.Equal((1, 0), (exit, fake.WebhooksCreated.Count));
    }

    [Fact]
    public async Task WebhookDelete_ByName_DeletesTheResolvedWebhook()
    {
        var fake = WithHook();

        await Run(fake, "--yes webhook delete Deploy");

        Assert.Equal([HookId], fake.WebhooksDeleted);
    }

    [Fact]
    public async Task WebhookLogList_NoWebhook_ReadsEveryWebhooksLog()
    {
        var fake = WithHook();

        var exit = await Run(fake, "webhook log list");

        Assert.Equal((0, (Guid?)null), (exit, Assert.Single(fake.WebhookLogReads)));
    }

    [Fact]
    public async Task WebhookLogList_ByName_ReadsThatWebhooksLog()
    {
        var fake = WithHook();

        await Run(fake, "webhook log list Deploy");

        Assert.Equal(HookId, Assert.Single(fake.WebhookLogReads));
    }

    [Fact]
    public async Task WebhookLogList_UnknownName_ReadsNoLog()
    {
        var fake = WithHook();

        var exit = await Run(fake, "webhook log list Nope");

        Assert.Equal((1, 0), (exit, fake.WebhookLogReads.Count));
    }

    [Fact]
    public void Headers_RepeatedNameInOtherCase_KeepsOneHeader()
    {
        var headers = WebhookOptions.Headers(["X-Key=a", "x-key=b"]);

        Assert.Equal("b", Assert.Single(headers).Value);
    }

    [Fact]
    public void Headers_NotGiven_IsEmpty()
    {
        Assert.Empty(WebhookOptions.Headers(null));
    }
}
