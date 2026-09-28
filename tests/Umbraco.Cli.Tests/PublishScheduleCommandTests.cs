using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the scheduled publish and publish-descendants --wait options (#90):
/// --publish-at/--unpublish-at are parsed and threaded to the client's schedule, and --wait is
/// threaded to the descendants call. Client behaviour is covered by <see cref="ContentWriteClientTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class PublishScheduleCommandTests
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

    /// <summary>Runs the command and returns the <c>message</c> of its JSON success envelope.</summary>
    private static async Task<System.Text.Json.JsonElement> DataOf(string args)
    {
        var root = BuildRoot(
            new FakeUmbracoManagementClient
            {
                PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
            }
        );
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            await root.Parse(args).InvokeAsync();
        }
        finally
        {
            Console.SetOut(orig);
        }
        return System
            .Text.Json.JsonDocument.Parse(sw.ToString())
            .RootElement.GetProperty("data")
            .Clone();
    }

    private const string Auth = "--host https://x --token t --output json";

    [Fact]
    public void Publish_WithoutSchedule_SaysPublished()
    {
        var message = ContentPublishCommand.SuccessMessage(null, null, null);

        Assert.Equal("Content item published.", message);
    }

    [Fact]
    public void Publish_WithPublishAt_SaysScheduledNotPublished()
    {
        // #239: a scheduled publish said "Content item published." while the item stayed a draft.
        var message = ContentPublishCommand.SuccessMessage(
            DateTimeOffset.Parse("2026-01-01T09:00:00Z"),
            null,
            ["en-US", "da-DK"]
        );

        Assert.Equal("Scheduled to publish at 2026-01-01T09:00:00Z (en-US, da-DK).", message);
    }

    [Fact]
    public void Publish_WithOnlyUnpublishAt_SaysPublishedAndNamesTheUnpublishTime()
    {
        var message = ContentPublishCommand.SuccessMessage(
            null,
            DateTimeOffset.Parse("2026-02-01T18:30:00Z"),
            null
        );

        Assert.Equal(
            "Content item published; scheduled to unpublish at 2026-02-01T18:30:00Z.",
            message
        );
    }

    [Fact]
    public async Task Publish_Scheduled_DataSaysNotPublishedYet()
    {
        // JSON callers get the same fact the message states (#239): scheduled is not published.
        var data = await DataOf(
            $"{Auth} content publish {Guid.NewGuid()} --publish-at 2026-01-01T09:00:00Z"
        );

        Assert.False(data.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task Publish_WithSchedule_ThreadsPublishAndUnpublishTimes()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();

        var exit = await Run(
            root,
            $"{Auth} content publish {id} --publish-at 2026-01-01T09:00:00Z --unpublish-at 2026-02-01T18:30:00Z"
        );

        Assert.Equal(0, exit);
        Assert.Equal(
            DateTimeOffset.Parse("2026-01-01T09:00:00Z"),
            fake.LastPublishSchedule!.Value.PublishAt
        );
        Assert.Equal(
            DateTimeOffset.Parse("2026-02-01T18:30:00Z"),
            fake.LastPublishSchedule!.Value.UnpublishAt
        );
    }

    [Fact]
    public async Task Publish_WithoutSchedule_LeavesTimesNull()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} content publish {Guid.NewGuid()}");

        Assert.Equal(0, exit);
        Assert.Null(fake.LastPublishSchedule!.Value.PublishAt);
        Assert.Null(fake.LastPublishSchedule!.Value.UnpublishAt);
    }

    /// <summary>Runs the command against <paramref name="fake"/> and returns its JSON envelope's <c>data</c>.</summary>
    private static async Task<System.Text.Json.JsonElement> DataOf(
        FakeUmbracoManagementClient fake,
        string args
    )
    {
        var root = BuildRoot(fake);
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            await root.Parse(args).InvokeAsync();
        }
        finally
        {
            Console.SetOut(orig);
        }
        return System
            .Text.Json.JsonDocument.Parse(sw.ToString())
            .RootElement.GetProperty("data")
            .Clone();
    }

    /// <summary>A fake whose publish succeeds, for a document with the given cultures (none = invariant).</summary>
    private static FakeUmbracoManagementClient Document(params string[] cultures) =>
        new()
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
            PublishCulturesHandler = _ => UmbracoResponse<IReadOnlyList<string>>.Success(cultures),
        };

    [Fact]
    public async Task Publish_VariantDocumentWithoutCulture_DataListsEveryCulture()
    {
        // #325: publishing every culture reported no cultures at all.
        var data = await DataOf(
            Document("en-US", "da-DK", "ja-JP"),
            $"{Auth} content publish {Guid.NewGuid()}"
        );

        Assert.Equal(
            ["en-US", "da-DK", "ja-JP"],
            data.GetProperty("cultures").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task Publish_VariantDocumentWithoutCulture_PublishesTheCulturesItReports()
    {
        var fake = Document("en-US", "da-DK");

        await DataOf(fake, $"{Auth} content publish {Guid.NewGuid()}");

        Assert.Equal(["en-US", "da-DK"], fake.StateCalls.Single().Cultures);
    }

    [Fact]
    public async Task Publish_WithCulture_DataListsOnlyTheNamedCultures()
    {
        var data = await DataOf(
            Document("en-US", "da-DK"),
            $"{Auth} content publish {Guid.NewGuid()} --culture da-DK"
        );

        Assert.Equal(
            ["da-DK"],
            data.GetProperty("cultures").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task Publish_InvariantDocument_DataCarriesNullCultures()
    {
        // Present and null, not absent: an absent field means "unknown" in the output contract.
        var data = await DataOf(Document(), $"{Auth} content publish {Guid.NewGuid()}");

        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("cultures").ValueKind);
    }

    [Fact]
    public async Task Publish_InvariantDocumentWithCulture_DataCarriesNullCultures()
    {
        // #362: --culture en-US on an invariant document was echoed as ["en-US"].
        var data = await DataOf(
            Document(),
            $"{Auth} content publish {Guid.NewGuid()} --culture en-US"
        );

        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("cultures").ValueKind);
    }

    [Fact]
    public async Task Publish_InvariantDocumentWithCulture_PublishesItWhole()
    {
        var fake = Document();

        await DataOf(fake, $"{Auth} content publish {Guid.NewGuid()} --culture en-US");

        Assert.Null(fake.StateCalls.Single().Cultures);
    }

    [Fact]
    public async Task Publish_WithoutSchedule_DataCarriesNullScheduleTimes()
    {
        var data = await DataOf(Document("en-US"), $"{Auth} content publish {Guid.NewGuid()}");

        Assert.Equal(
            (System.Text.Json.JsonValueKind.Null, System.Text.Json.JsonValueKind.Null),
            (data.GetProperty("publishAt").ValueKind, data.GetProperty("unpublishAt").ValueKind)
        );
    }

    [Fact]
    public async Task Publish_CulturesCannotBeRead_FailsWithoutPublishing()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
            PublishCulturesHandler = _ =>
                UmbracoResponse<IReadOnlyList<string>>.Failure(404, "Content item not found"),
        };

        var exit = await Run(BuildRoot(fake), $"{Auth} content publish {Guid.NewGuid()}");

        Assert.Equal((1, 0), (exit, fake.StateCalls.Count));
    }

    // ── unpublish reports its cultures too (the unpublish side of #325) ────────

    /// <summary>A fake whose unpublish succeeds, for a document with the given cultures (none = invariant).</summary>
    /// <param name="cultures">The document's cultures; none means an invariant document.</param>
    /// <returns>The configured fake.</returns>
    private static FakeUmbracoManagementClient Unpublishable(params string[] cultures) =>
        new()
        {
            UnpublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
            PublishCulturesHandler = _ => UmbracoResponse<IReadOnlyList<string>>.Success(cultures),
        };

    [Fact]
    public async Task Unpublish_VariantDocumentWithoutCulture_DataListsEveryCulture()
    {
        var data = await DataOf(
            Unpublishable("en-US", "da-DK"),
            $"{Auth} content unpublish {Guid.NewGuid()} --yes"
        );

        Assert.Equal(
            ["en-US", "da-DK"],
            data.GetProperty("cultures").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task Unpublish_VariantDocumentWithoutCulture_UnpublishesTheCulturesItReports()
    {
        var fake = Unpublishable("en-US", "da-DK");

        await DataOf(fake, $"{Auth} content unpublish {Guid.NewGuid()} --yes");

        Assert.Equal(["en-US", "da-DK"], fake.StateCalls.Single().Cultures);
    }

    [Fact]
    public async Task Unpublish_WithCulture_DataListsOnlyTheNamedCultures()
    {
        var data = await DataOf(
            Unpublishable("en-US", "da-DK"),
            $"{Auth} content unpublish {Guid.NewGuid()} --culture da-DK --yes"
        );

        Assert.Equal(
            ["da-DK"],
            data.GetProperty("cultures").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task Unpublish_InvariantDocument_DataCarriesNullCultures()
    {
        // Present and null, not absent: an absent field means "unknown" in the output contract.
        var data = await DataOf(
            Unpublishable(),
            $"{Auth} content unpublish {Guid.NewGuid()} --yes"
        );

        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("cultures").ValueKind);
    }

    [Fact]
    public async Task Unpublish_WithoutCulture_DataLeavesOutDraftCultures()
    {
        // #362: a culture already in Draft was listed as unpublished.
        var fake = Unpublishable("en-US", "da-DK");
        fake.DraftCultures.Add("da-DK");

        var data = await DataOf(fake, $"{Auth} content unpublish {Guid.NewGuid()} --yes");

        Assert.Equal(
            ["en-US"],
            data.GetProperty("cultures").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task Unpublish_WithoutCulture_SendsOnlyThePublishedCultures()
    {
        var fake = Unpublishable("en-US", "da-DK");
        fake.DraftCultures.Add("da-DK");

        await DataOf(fake, $"{Auth} content unpublish {Guid.NewGuid()} --yes");

        Assert.Equal(["en-US"], fake.StateCalls.Single().Cultures);
    }

    [Fact]
    public async Task Unpublish_NamedCultureAlreadyDraft_DataCarriesAnEmptyList()
    {
        var fake = Unpublishable("en-US", "da-DK");
        fake.DraftCultures.Add("da-DK");

        var data = await DataOf(
            fake,
            $"{Auth} content unpublish {Guid.NewGuid()} --culture da-DK --yes"
        );

        Assert.Equal(0, data.GetProperty("cultures").GetArrayLength());
    }

    [Fact]
    public async Task Unpublish_NamedCultureAlreadyDraft_StillSendsTheRequest()
    {
        var fake = Unpublishable("en-US", "da-DK");
        fake.DraftCultures.Add("da-DK");

        await DataOf(fake, $"{Auth} content unpublish {Guid.NewGuid()} --culture da-DK --yes");

        Assert.Equal(["da-DK"], fake.StateCalls.Single().Cultures);
    }

    [Fact]
    public async Task Unpublish_InvariantDocumentWithCulture_DataCarriesNullCultures()
    {
        // #362: --culture en-US on an invariant document was echoed as ["en-US"].
        var data = await DataOf(
            Unpublishable(),
            $"{Auth} content unpublish {Guid.NewGuid()} --culture en-US --yes"
        );

        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("cultures").ValueKind);
    }

    [Fact]
    public async Task Unpublish_CulturesCannotBeRead_FailsWithoutUnpublishing()
    {
        var fake = new FakeUmbracoManagementClient
        {
            UnpublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
            PublishCulturesHandler = _ =>
                UmbracoResponse<IReadOnlyList<string>>.Failure(404, "Content item not found"),
        };

        var exit = await Run(BuildRoot(fake), $"{Auth} content unpublish {Guid.NewGuid()} --yes");

        Assert.Equal((1, 0), (exit, fake.StateCalls.Count));
    }

    [Fact]
    public async Task PublishDescendants_Wait_ThreadsWaitFlag()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);
        var id = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} content publish-descendants {id} --wait");

        Assert.Equal(0, exit);
        Assert.True(fake.LastPublishDescendantsArgs!.Value.Wait);
        Assert.Equal(id, fake.LastPublishDescendantsArgs!.Value.Id);
    }

    [Fact]
    public async Task PublishDescendants_WithoutWait_DoesNotWait()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} content publish-descendants {Guid.NewGuid()}");

        Assert.Equal(0, exit);
        Assert.False(fake.LastPublishDescendantsArgs!.Value.Wait);
    }
}
