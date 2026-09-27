using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>media export</c> / <c>diff</c> / <c>apply</c> through the real command tree (#226): an export
/// diffs clean against the instance it came from, and a snapshot cannot be read from stdin.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class MediaPipelineCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"media-cmd-{Guid.NewGuid()}");

    /// <summary>Removes the test's snapshot directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

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
        root.Add(MediaCommand.Build(new CommandExecutor(factory, new NonInteractivePrompt())));
        return root;
    }

    private static async Task<(string Stdout, string Stderr, int Exit)> Run(
        RootCommand root,
        string args
    )
    {
        var (out_, err) = (new StringWriter(), new StringWriter());
        var (origOut, origErr) = (Console.Out, Console.Error);
        Console.SetOut(out_);
        Console.SetError(err);
        try
        {
            var exit = await root.Parse(args).InvokeAsync();
            return (out_.ToString(), err.ToString(), exit);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    /// <summary>A live instance holding one image.</summary>
    private static FakeUmbracoManagementClient LiveWithImage()
    {
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.MediaSnapshotTree.Add(new ContentTreeNode(id, null));
        fake.MediaRaw[id] = JsonNode.Parse(
            $$$"""
            {"id":"{{{id}}}","mediaType":{"id":"{{{Guid.NewGuid()}}}"},"values":[
              {"alias":"umbracoFile","value":{"src":"/media/abc/photo.jpg"}},
              {"alias":"umbracoBytes","value":10}],
             "variants":[{"culture":null,"name":"Photo"}]}
            """
        )!;
        fake.MediaFiles["/media/abc/photo.jpg"] = Encoding.UTF8.GetBytes("0123456789");
        return fake;
    }

    [Fact]
    public async Task ExportThenDiff_AgainstTheSameInstance_ReportsNothing()
    {
        // Arrange
        var fake = LiveWithImage();
        var root = BuildRoot(fake);
        var (_, exportErr, exportExit) = await Run(
            root,
            $"--host https://x --token t --output json media export --out {_dir}"
        );
        Assert.True(exportExit == 0, exportErr);

        // Act
        var (stdout, _, exit) = await Run(
            BuildRoot(fake),
            $"--host https://x --token t --output json media diff {_dir}"
        );

        // Assert
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Empty(doc.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Diff_FromStdin_IsAnInvalidArgument()
    {
        var (_, stderr, exit) = await Run(
            BuildRoot(new FakeUmbracoManagementClient()),
            "--host https://x --token t --output json media diff -"
        );

        Assert.Multiple(
            () => Assert.Equal(1, exit),
            () => Assert.Contains("invalid_argument", stderr)
        );
    }
}
