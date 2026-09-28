using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// End-to-end wiring of the <c>schema export</c> command (#68): parsed from the real command
/// tree and run through the real <see cref="CommandContextFactory"/> with a fake client, so it
/// covers registration, the exporter call, and the <c>--out</c> vs stdout render branches.
/// </summary>
[Collection("ConsoleCapture")]
public class SchemaExportCommandTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory : IUmbracoManagementClientFactory
    {
        private readonly IUmbracoManagementClient _client;

        public FakeClientFactory(IUmbracoManagementClient client) => _client = client;

        public IUmbracoManagementClient Create(HttpClient http) => _client;
    }

    private sealed class NonInteractivePrompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => false;
    }

    /// <summary>Builds a root command with the real schema tree over a fake client.</summary>
    private static RootCommand BuildRoot(IUmbracoManagementClient client)
    {
        var stub = new StubHttpClientFactory();
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"umbraco-schema-test-{Guid.NewGuid()}.json")
        );
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            new UmbracoAuthService(stub),
            stub,
            global,
            new FakeClientFactory(client),
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, new NonInteractivePrompt());

        var root = new RootCommand();
        global.AddTo(root);
        root.Add(SchemaCommand.Build(executor));
        return root;
    }

    private static async Task<(string stdout, int exit)> Capture(RootCommand root, string args)
    {
        var outSw = new StringWriter();
        var origOut = Console.Out;
        Console.SetOut(outSw);
        try
        {
            var exit = await root.Parse(args).InvokeAsync();
            return (outSw.ToString(), exit);
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    private static FakeUmbracoManagementClient OneDocTypeInstance(out Guid id)
    {
        var docId = Guid.NewGuid();
        id = docId;
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = docId, Alias = "blogPost" });
        fake.DocumentTypeRaw[docId] = JsonNode.Parse(
            $$"""{"id":"{{docId}}","alias":"blogPost","properties":[{"alias":"body"}]}"""
        )!;
        return fake;
    }

    [Fact]
    public async Task Export_ToFile_WritesBareSnapshotAndReportsCounts()
    {
        var fake = OneDocTypeInstance(out _);
        var root = BuildRoot(fake);
        var outFile = Path.Combine(Path.GetTempPath(), $"snap-{Guid.NewGuid()}.json");

        try
        {
            var (stdout, exit) = await Capture(
                root,
                $"--host https://example.com --token t --output json schema export --out {outFile}"
            );

            Assert.Equal(0, exit);
            Assert.True(File.Exists(outFile));
            // The file is a BARE snapshot (no envelope) and preserves the rich body verbatim.
            var snap = SchemaSnapshot.FromJson(await File.ReadAllTextAsync(outFile));
            Assert.Equal("blogPost", (string?)snap.DocumentTypes!.Single()["alias"]);
            Assert.Equal("body", (string?)snap.DocumentTypes!.Single()["properties"]![0]!["alias"]);
            // stdout carries a structured count summary, not the whole snapshot.
            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal(
                1,
                doc.RootElement.GetProperty("data").GetProperty("documentTypes").GetInt32()
            );
        }
        finally
        {
            if (File.Exists(outFile))
                File.Delete(outFile);
        }
    }

    [Fact]
    public async Task Export_NoOut_WritesSnapshotToStdoutEnvelope()
    {
        var fake = OneDocTypeInstance(out _);
        var root = BuildRoot(fake);

        var (stdout, exit) = await Capture(
            root,
            "--host https://example.com --token t --output json schema export"
        );

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var docTypes = doc.RootElement.GetProperty("data").GetProperty("documentTypes");
        Assert.Equal("blogPost", docTypes[0].GetProperty("alias").GetString());
    }
}
