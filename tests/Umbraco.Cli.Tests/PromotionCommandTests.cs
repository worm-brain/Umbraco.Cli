using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The command-level ends of the Phase 2 id work: a <c>--json-body</c> create reports its id
/// (#204) and honours <c>--id</c> (#218), and <c>media upload</c> passes its <c>--id</c> and
/// <c>--value</c> through (#226, #220).
/// </summary>
[Collection("ConsoleCapture")]
public class PromotionCommandTests
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
        var executor = new CommandExecutor(factory, new NonInteractivePrompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(DataTypesCommand.Build(executor));
        root.Add(MediaCommand.Build(executor));
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

    private const string Auth = "--host https://x --token t --output json";

    private static string TempFile(string contents, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"p2-{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public async Task DataTypesCreate_JsonBodyWithId_CreatesWithThatIdAndReportsIt()
    {
        var id = Guid.NewGuid();
        var body = TempFile(
            """{"name":"Homepage Blocks","editorAlias":"Umbraco.BlockList"}""",
            ".json"
        );
        var fake = new FakeUmbracoManagementClient();
        try
        {
            var (stdout, exit) = await Run(
                BuildRoot(fake),
                $"{Auth} data-type create --id {id} --json-body {body}"
            );

            Assert.Equal(0, exit);
            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal(
                (id.ToString(), id.ToString()),
                (
                    doc.RootElement.GetProperty("data").GetProperty("id").GetString(),
                    (string?)Assert.Single(fake.RawWrites).Body!["id"]
                )
            );
        }
        finally
        {
            File.Delete(body);
        }
    }

    [Fact]
    public async Task DataTypesCreate_IdDisagreesWithBody_FailsWithoutWriting()
    {
        var body = TempFile($$"""{"id":"{{Guid.NewGuid()}}","name":"X"}""", ".json");
        var fake = new FakeUmbracoManagementClient();
        try
        {
            var (_, exit) = await Run(
                BuildRoot(fake),
                $"{Auth} data-type create --id {Guid.NewGuid()} --json-body {body}"
            );

            Assert.Equal((1, 0), (exit, fake.RawWrites.Count));
        }
        finally
        {
            File.Delete(body);
        }
    }

    [Fact]
    public async Task DataTypesCreate_JsonBody_ReturnsTheItemAsSaved()
    {
        // #285: the saved item has configuration the body did not, and the output shows it.
        var id = Guid.NewGuid();
        var body = TempFile("""{"name":"Blocks","editorAlias":"Umbraco.BlockList"}""", ".json");
        var fake = new FakeUmbracoManagementClient();
        fake.DataTypeRaw[id] = JsonNode.Parse(
            $$"""{"id":"{{id}}","name":"Blocks","values":[{"alias":"blocks","value":[]}]}"""
        )!;
        try
        {
            var (stdout, _) = await Run(
                BuildRoot(fake),
                $"{Auth} data-type create --id {id} --json-body {body}"
            );

            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal(
                "blocks",
                doc.RootElement.GetProperty("data")
                    .GetProperty("values")[0]
                    .GetProperty("alias")
                    .GetString()
            );
        }
        finally
        {
            File.Delete(body);
        }
    }

    [Fact]
    public async Task DataTypesCreate_FlagsOnly_CreatesWithTheIdAndReturnsTheItemAsSaved()
    {
        // #285: the flag-built create used to echo its request; it now reads the item back.
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DataTypeRaw[id] = JsonNode.Parse(
            $$"""{"id":"{{id}}","name":"Text","values":[{"alias":"maxChars","value":50}]}"""
        )!;

        var (stdout, _) = await Run(
            BuildRoot(fake),
            $"{Auth} data-type create --id {id} --name Text --editor-alias Umbraco.TextBox --editor-ui-alias Umb.PropertyEditorUi.TextBox"
        );

        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(
            ((Guid?)id, "maxChars"),
            (
                Assert.Single(fake.DataTypeCreates).Id,
                doc.RootElement.GetProperty("data")
                    .GetProperty("values")[0]
                    .GetProperty("alias")
                    .GetString()
            )
        );
    }

    [Fact]
    public async Task DataTypesCreate_FlagCreateFails_ExitsOne()
    {
        var fake = new FakeUmbracoManagementClient
        {
            DataTypeCreateFailure = UmbracoResponse<DataTypeResponse>.Failure(400, "Name taken"),
        };

        var (_, exit) = await Run(
            BuildRoot(fake),
            $"{Auth} data-type create --name Text --editor-alias Umbraco.TextBox --editor-ui-alias Umb.PropertyEditorUi.TextBox"
        );

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task MediaUpload_PassesIdAndValuesToTheClient()
    {
        var id = Guid.NewGuid();
        var file = TempFile("%PDF", ".pdf");
        var fake = new FakeUmbracoManagementClient();
        try
        {
            var (_, exit) = await Run(
                BuildRoot(fake),
                $"{Auth} media upload {file} --media-type brochure --id {id} --value title=Brochure --value pages=12"
            );

            Assert.Equal(0, exit);
            Assert.Equal(id, fake.LastUpload!.Value.Id);
            Assert.Equal(
                ["title=Brochure", "pages=12"],
                fake.LastUpload!.Value.Values!.Select(v => $"{v.Alias}={v.Value}")
            );
        }
        finally
        {
            File.Delete(file);
        }
    }
}
