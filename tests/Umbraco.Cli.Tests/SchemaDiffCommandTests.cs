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
/// End-to-end wiring of <c>schema diff</c> (#68): loads a snapshot file, exports the live
/// schema via the fake, and emits a row per actionable change (empty when they match).
/// </summary>
[Collection("ConsoleCapture")]
public class SchemaDiffCommandTests
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
        root.Add(SchemaCommand.Build(new CommandExecutor(factory, new NonInteractivePrompt())));
        return root;
    }

    private static async Task<(string stdout, int exit)> Run(RootCommand root, string args)
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

    /// <summary>Writes a snapshot file with one document type and returns its path + id.</summary>
    private static string WriteSnapshot(Guid id, string alias, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"snap-{Guid.NewGuid()}.json");
        var snap = new SchemaSnapshot
        {
            DocumentTypes =
            [
                JsonNode.Parse($$"""{"id":"{{id}}","alias":"{{alias}}","name":"{{name}}"}""")!,
            ],
        };
        File.WriteAllText(path, snap.ToJson());
        return path;
    }

    [Fact]
    public async Task Diff_LiveMissingEntity_ReportsAdded()
    {
        var id = Guid.NewGuid();
        var path = WriteSnapshot(id, "blogPost", "Blog Post");
        var fake = new FakeUmbracoManagementClient(); // empty instance
        var root = BuildRoot(fake);

        try
        {
            var (stdout, exit) = await Run(
                root,
                $"--host https://x --token t --output json schema diff {path}"
            );

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(stdout);
            var rows = doc.RootElement.GetProperty("data");
            var row = Assert.Single(rows.EnumerateArray().ToList());
            Assert.Equal("Added", row.GetProperty("change").GetString());
            Assert.Equal("blogPost", row.GetProperty("identity").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Diff_ChangedEntity_IsATypedRowNamingTheChange()
    {
        // #229: a boolean idMismatch, a null note, and the changed field by name.
        var id = Guid.NewGuid();
        var path = WriteSnapshot(id, "blogPost", "Blog Post");
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = id, Alias = "blogPost" });
        fake.DocumentTypeRaw[id] = JsonNode.Parse(
            $$"""{"id":"{{id}}","alias":"blogPost","name":"Blog"}"""
        )!;

        try
        {
            var (stdout, _) = await Run(
                BuildRoot(fake),
                $"--host https://x --token t --output json schema diff {path}"
            );

            using var doc = JsonDocument.Parse(stdout);
            var row = Assert.Single(doc.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal(
                $$"""{"kind":"documentType","change":"Changed","identity":"blogPost","desiredId":"{{id}}","currentId":"{{id}}","idMismatch":false,"note":null,"changes":["name"]}""",
                JsonSerializer.Serialize(row)
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Diff_InstanceMatchesSnapshot_ReportsNoChanges()
    {
        var id = Guid.NewGuid();
        var path = WriteSnapshot(id, "blogPost", "Blog Post");
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = id, Alias = "blogPost" });
        fake.DocumentTypeRaw[id] = JsonNode.Parse(
            $$"""{"id":"{{id}}","alias":"blogPost","name":"Blog Post"}"""
        )!;
        var root = BuildRoot(fake);

        try
        {
            var (stdout, exit) = await Run(
                root,
                $"--host https://x --token t --output json schema diff {path}"
            );

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(stdout);
            // No actionable changes -> empty rows array (a clean CI "no drift" signal).
            Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray().ToList());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
