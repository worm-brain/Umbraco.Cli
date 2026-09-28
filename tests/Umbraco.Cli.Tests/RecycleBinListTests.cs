using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>content list --trashed</c> and <c>media list --trashed</c> (#364): the recycle bin can be
/// seen before <c>empty-recycle-bin --yes</c> deletes it, and a trashed id found to restore.
/// </summary>
[Collection("ConsoleCapture")]
public class RecycleBinListTests
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

    private const string Auth = "--host https://x --token t --output json";

    /// <summary>Runs a command against <paramref name="client"/> and returns its <c>data</c>.</summary>
    /// <param name="client">The fake client.</param>
    /// <param name="args">The command line, after the auth options.</param>
    /// <returns>The envelope's <c>data</c>.</returns>
    private static async Task<JsonElement> DataOf(IUmbracoManagementClient client, string args)
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

        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            await root.Parse($"{Auth} {args}").InvokeAsync();
        }
        finally
        {
            Console.SetOut(orig);
        }
        return JsonDocument.Parse(sw.ToString()).RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task ContentList_Trashed_ListsTheRecycleBin()
    {
        var trashed = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.TrashedContent.Add(new ContentItemResponse { Id = trashed, IsTrashed = true });
        fake.ContentChildren.Add(new ContentItemResponse { Id = Guid.NewGuid() });

        var data = await DataOf(fake, "content list --trashed");

        Assert.Equal(trashed, Assert.Single(data.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ContentList_WithoutTrashed_ListsTheContentTree()
    {
        var live = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.TrashedContent.Add(new ContentItemResponse { Id = Guid.NewGuid() });
        fake.ContentChildren.Add(new ContentItemResponse { Id = live });

        var data = await DataOf(fake, "content list");

        Assert.Equal(live, Assert.Single(data.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task MediaList_Trashed_ListsTheRecycleBin()
    {
        var trashed = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.TrashedMedia.Add(new MediaItemResponse { Id = trashed });

        var data = await DataOf(fake, "media list --trashed");

        Assert.Equal(trashed, Assert.Single(data.EnumerateArray()).GetProperty("id").GetGuid());
    }

    // ── the client reads the recycle-bin tree ─────────────────────────────────

    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Type = Guid.NewGuid();

    [Fact]
    public async Task GetContentRecycleBinAsync_TopLevel_ReadsTheBinRootAsTrashedRows()
    {
        var handler = Wire.Routed(
            ($"/document-type/{Type}", """{ "alias": "blogPost" }"""),
            (
                "recycle-bin/document/root",
                $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "documentType": { "id": "{{Type}}" }, "variants": [ { "name": "Old post", "state": "Published" } ] } ] }"""
            )
        );

        var result = await Wire.Client(handler)
            .GetContentRecycleBinAsync(ct: CancellationToken.None);

        var row = Assert.Single(result.Data!.Items);
        Assert.Equal(
            (Item, "Old post", "blogPost", true, false),
            (row.Id, row.Name, row.DocumentType!.Alias, row.IsTrashed, row.IsPublished)
        );
    }

    [Fact]
    public async Task GetContentRecycleBinAsync_WithParent_ReadsTheBinChildren()
    {
        var parent = Guid.NewGuid();
        var handler = Wire.Routed(("recycle-bin/document/children", """{ "total": 0, "items": [] }"""));

        await Wire.Client(handler).GetContentRecycleBinAsync(parent, ct: CancellationToken.None);

        Assert.NotNull(
            handler.FirstMatching(r =>
                r.Uri.AbsoluteUri.Contains("recycle-bin/document/children")
                && r.Uri.Query.Contains(parent.ToString(), StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    [Fact]
    public async Task GetMediaRecycleBinAsync_TopLevel_ReadsTheBinRoot()
    {
        var handler = Wire.Routed(
            (
                "recycle-bin/media/root",
                $$"""{ "total": 1, "items": [ { "id": "{{Item}}", "variants": [ { "name": "Old.pdf" } ] } ] }"""
            )
        );

        var result = await Wire.Client(handler).GetMediaRecycleBinAsync(ct: CancellationToken.None);

        Assert.Equal("Old.pdf", Assert.Single(result.Data!.Items).Name);
    }
}
