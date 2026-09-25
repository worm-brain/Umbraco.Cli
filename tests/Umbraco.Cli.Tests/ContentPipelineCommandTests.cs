using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The structured output of <c>content diff</c> and <c>content apply</c> (#229, #223): rows are the
/// change records themselves, with real nulls, a <c>changes</c> list, and the publish steps apply
/// adds.
/// </summary>
[Collection("ConsoleCapture")]
public class ContentPipelineCommandTests
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

    private sealed class NonInteractivePrompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => false;
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
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(ContentCommand.Build(new CommandExecutor(factory, new NonInteractivePrompt())));
        return root;
    }

    private static async Task<(string Stdout, int Exit)> Run(RootCommand root, string args)
    {
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exit = await root.Parse(args).InvokeAsync();
            return (sw.ToString(), exit);
        }
        finally
        {
            Console.SetOut(orig);
        }
    }

    private static readonly Guid Home = Guid.NewGuid();

    private static JsonNode HomeBody(string title, string state) =>
        JsonNode.Parse(
            $$"""
            {"id":"{{Home}}","values":[{"alias":"title","culture":"en-US","segment":null,"value":"{{title}}"}],
             "variants":[{"culture":"en-US","segment":null,"name":"Home","state":"{{state}}"}]}
            """
        )!;

    /// <summary>A live instance holding Home as an unedited draft.</summary>
    private static FakeUmbracoManagementClient LiveDraftHome()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };
        fake.DocumentTree.Add(new ContentTreeNode(Home, null));
        fake.DocumentRaw[Home] = HomeBody("Hello", "Draft");
        return fake;
    }

    /// <summary>A snapshot where Home is published with a new title.</summary>
    private static string WriteSnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"content-{Guid.NewGuid()}.json");
        var snapshot = new ContentSnapshot
        {
            Documents =
            [
                new ContentNode
                {
                    Id = Home,
                    Parent = null,
                    Body = HomeBody("Hello, world", "Published"),
                },
            ],
        };
        File.WriteAllText(path, snapshot.ToJson());
        return path;
    }

    [Fact]
    public async Task Diff_ChangedDocument_RowNamesWhatChangedWithRealNulls()
    {
        var path = WriteSnapshot();
        try
        {
            var (stdout, exit) = await Run(
                BuildRoot(LiveDraftHome()),
                $"--host https://x --token t --output json content diff {path}"
            );

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(stdout);
            var row = Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal(
                """{"change":"Changed","id":"%ID%","parent":null,"changes":["values.title[en-US]","state[en-US]"]}""".Replace(
                    "%ID%",
                    Home.ToString()
                ),
                JsonSerializer.Serialize(row)
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Diff_ReportsItselfComplete()
    {
        var path = WriteSnapshot();
        try
        {
            var (stdout, _) = await Run(
                BuildRoot(LiveDraftHome()),
                $"--host https://x --token t --output json content diff {path}"
            );

            using var doc = JsonDocument.Parse(stdout);
            var meta = doc.RootElement.GetProperty("meta");
            Assert.Equal(
                (1, false),
                (meta.GetProperty("total").GetInt32(), meta.GetProperty("hasMore").GetBoolean())
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_ChangedPublishedDocument_UpdatesThenPublishesItsCulture()
    {
        var path = WriteSnapshot();
        try
        {
            var (stdout, exit) = await Run(
                BuildRoot(LiveDraftHome()),
                $"--host https://x --token t --output json content apply {path}"
            );

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(stdout);
            var rows = doc
                .RootElement.GetProperty("data")
                .EnumerateArray()
                .Select(r => JsonSerializer.Serialize(r))
                .ToList();
            Assert.Equal(
                [
                    $$"""{"operation":"update","id":"{{Home}}","status":"success","cultures":null}""",
                    $$"""{"operation":"publish","id":"{{Home}}","status":"success","cultures":["en-US"]}""",
                ],
                rows
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_NoState_OnlyUpdates()
    {
        var path = WriteSnapshot();
        var fake = LiveDraftHome();
        try
        {
            var (_, exit) = await Run(
                BuildRoot(fake),
                $"--host https://x --token t --output json content apply {path} --no-state"
            );

            Assert.Equal(0, exit);
            Assert.Empty(fake.StateCalls);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
