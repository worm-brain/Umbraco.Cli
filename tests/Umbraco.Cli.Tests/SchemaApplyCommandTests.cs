using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// End-to-end safety behaviour of <c>schema apply</c> (#68 / ADR 0005 §4): dry-run writes
/// nothing, the prune path is gated behind <c>--yes</c> non-interactively, and prune actually
/// deletes when confirmed.
/// </summary>
[Collection("ConsoleCapture")]
public class SchemaApplyCommandTests
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

    private static RootCommand BuildRoot(
        IUmbracoManagementClient client,
        IConfirmationPrompt prompt
    )
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
        root.Add(SchemaCommand.Build(new CommandExecutor(factory, prompt)));
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

    /// <summary>Writes a snapshot file containing a single document type.</summary>
    private static string WriteSnapshot(Guid id, string alias)
    {
        var path = Path.Combine(Path.GetTempPath(), $"snap-{Guid.NewGuid()}.json");
        var snap = new SchemaSnapshot
        {
            DocumentTypes = { JsonNode.Parse($$"""{"id":"{{id}}","alias":"{{alias}}"}""")! },
        };
        File.WriteAllText(path, snap.ToJson());
        return path;
    }

    /// <summary>A fake instance holding one live document type (for prune tests).</summary>
    private static FakeUmbracoManagementClient InstanceWith(Guid id, string alias)
    {
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = id, Alias = alias });
        fake.DocumentTypeRaw[id] = JsonNode.Parse($$"""{"id":"{{id}}","alias":"{{alias}}"}""")!;
        return fake;
    }

    [Fact]
    public async Task Apply_DryRun_WritesNothing()
    {
        var id = Guid.NewGuid();
        var path = WriteSnapshot(id, "blogPost");
        var fake = new FakeUmbracoManagementClient(); // empty live -> snapshot doc type is an "add"
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: false));

        try
        {
            var exit = await Run(
                root,
                $"--host https://x --token t --output json --dry-run schema apply {path}"
            );

            Assert.Equal(0, exit);
            Assert.Empty(fake.RawWrites); // dry-run must not send any write
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_Prune_NonInteractiveWithoutYes_IsRefused()
    {
        // Prune is destructive; non-interactively it must abort (exit 2) and touch nothing.
        var liveId = Guid.NewGuid();
        var path = WriteSnapshot(Guid.NewGuid(), "keep");
        var fake = InstanceWith(liveId, "legacy"); // live-only entity that prune would delete
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: false));

        try
        {
            var exit = await Run(
                root,
                $"--host https://x --token t --output json schema apply {path} --prune"
            );

            Assert.Equal(2, exit);
            Assert.Empty(fake.SchemaDeletedIds); // nothing deleted without confirmation
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_PruneWithForceAndYes_DeletesLiveOnlyEntity()
    {
        var liveId = Guid.NewGuid();
        // Snapshot contains a DIFFERENT doc type, so the live "legacy" one is a prune candidate.
        // A document type always needs --force: its documents go with it (#252).
        var path = WriteSnapshot(Guid.NewGuid(), "keep");
        var fake = InstanceWith(liveId, "legacy");
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: false));

        try
        {
            var exit = await Run(
                root,
                $"--host https://x --token t --output json schema apply {path} --prune --force --yes"
            );

            Assert.Equal(0, exit);
            Assert.Contains(liveId, fake.SchemaDeletedIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_PruneOfADocumentTypeWithoutForce_IsRefusedAndAppliesNothing()
    {
        // #252: pruning a document type deletes its documents. Refused before the first write,
        // so the create for "keep" is not applied either.
        var liveId = Guid.NewGuid();
        var path = WriteSnapshot(Guid.NewGuid(), "keep");
        var fake = InstanceWith(liveId, "legacy");
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: false));

        try
        {
            var exit = await Run(
                root,
                $"--host https://x --token t --output json schema apply {path} --prune --yes"
            );

            Assert.Equal(2, exit);
            Assert.Empty(fake.SchemaDeletedIds);
            Assert.Empty(fake.RawWrites);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_PruneDryRun_MarksTheInUseDeleteInsteadOfRefusing()
    {
        var liveId = Guid.NewGuid();
        var path = WriteSnapshot(Guid.NewGuid(), "keep");
        var fake = InstanceWith(liveId, "legacy");
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: false));
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);

        try
        {
            var exit = await root.Parse(
                    $"--host https://x --token t --output json --dry-run schema apply {path} --prune"
                )
                .InvokeAsync();

            Assert.Equal(0, exit);
            Assert.Contains("needs --force", sw.ToString());
        }
        finally
        {
            Console.SetOut(orig);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Apply_Default_CreatesMissingEntity()
    {
        var id = Guid.NewGuid();
        var path = WriteSnapshot(id, "blogPost");
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: false));

        try
        {
            var exit = await Run(
                root,
                $"--host https://x --token t --output json schema apply {path}"
            );

            Assert.Equal(0, exit);
            var write = Assert.Single(fake.RawWrites);
            Assert.Equal("documentType", write.Kind);
            Assert.Null(write.Id); // a create, not an update
        }
        finally
        {
            File.Delete(path);
        }
    }
}
