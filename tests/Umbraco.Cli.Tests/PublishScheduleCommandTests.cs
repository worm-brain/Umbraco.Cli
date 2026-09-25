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
    private static async Task<string?> MessageOf(string args)
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
            .RootElement.GetProperty("message")
            .GetString();
    }

    private const string Auth = "--host https://x --token t --output json";

    [Fact]
    public async Task Publish_WithoutSchedule_SaysPublished()
    {
        var message = await MessageOf($"{Auth} content publish {Guid.NewGuid()}");

        Assert.Equal("Content item published.", message);
    }

    [Fact]
    public async Task Publish_WithPublishAt_SaysScheduledNotPublished()
    {
        // #239: a scheduled publish said "Content item published." while the item stayed a draft.
        var message = await MessageOf(
            $"{Auth} content publish {Guid.NewGuid()} --publish-at 2026-01-01T09:00:00Z --cultures en-US da-DK"
        );

        Assert.Equal("Scheduled to publish at 2026-01-01T09:00:00Z (en-US, da-DK).", message);
    }

    [Fact]
    public async Task Publish_WithOnlyUnpublishAt_SaysPublishedAndNamesTheUnpublishTime()
    {
        var message = await MessageOf(
            $"{Auth} content publish {Guid.NewGuid()} --unpublish-at 2026-02-01T18:30:00Z"
        );

        Assert.Equal(
            "Content item published; scheduled to unpublish at 2026-02-01T18:30:00Z.",
            message
        );
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
