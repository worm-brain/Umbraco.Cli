using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.MediaTypes;
using Umbraco.Cli.Commands.MemberTypes;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The schema nouns' <c>get</c> prints the verbatim Management API body and <c>update --json-body</c>
/// takes it back (#250 Phase 5: #201, #207, #213, #221). These pin the command wiring: which raw
/// read a <c>get</c> uses, and what an update or create hands the client.
/// </summary>
[Collection("ConsoleCapture")]
public class SchemaRoundTripCommandTests
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

    private static readonly Guid Id = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    /// <summary>Builds the schema nouns over <paramref name="client"/>.</summary>
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
        var executor = new CommandExecutor(factory, new Prompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(ContentTypesCommand.Build(executor));
        root.Add(DataTypesCommand.Build(executor));
        root.Add(MediaTypesCommand.Build(executor));
        root.Add(MemberTypesCommand.Build(executor));
        root.Add(TemplatesCommand.Build(executor));
        return root;
    }

    /// <summary>Runs a command line, with <c>{body}</c> replaced by a temp file holding <paramref name="body"/>.</summary>
    /// <param name="fake">The fake client.</param>
    /// <param name="args">The command line.</param>
    /// <param name="body">The JSON body for <c>{body}</c>.</param>
    /// <returns>The exit code and stdout.</returns>
    private static async Task<(int Exit, string Out)> Run(
        FakeUmbracoManagementClient fake,
        string args,
        string body = "{}"
    )
    {
        var path = Path.Combine(Path.GetTempPath(), $"body-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, body);
        var (out_, err) = (Console.Out, Console.Error);
        var sw = new StringWriter();
        Console.SetOut(sw);
        Console.SetError(new StringWriter());
        try
        {
            var exit = await BuildRoot(fake)
                .Parse($"--host https://x --token t --output json {args.Replace("{body}", path)}")
                .InvokeAsync();
            return (exit, sw.ToString());
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
            File.Delete(path);
        }
    }

    // ── --schema is the JSON Schema; --example the live item ────────────────────

    [Theory]
    [InlineData("document-type create --schema", "CreateDocumentTypeRequestModel")]
    [InlineData("data-type update --schema", "UpdateDataTypeRequestModel")]
    [InlineData("template update --schema", "UpdateTemplateRequestModel")]
    public async Task Schema_PrintsTheRequestModelsJsonSchemaWithoutCallingTheServer(
        string command,
        string model
    )
    {
        // docs/conventions.md 4.1: --schema means the JSON Schema everywhere, offline.
        var fake = new FakeUmbracoManagementClient();

        var (exit, output) = await Run(fake, command);

        Assert.Equal((0, model), (exit, JsonNode.Parse(output)!["title"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Example_PrintsARealItemFromTheInstance()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = Id, Alias = "blogPost" });
        fake.DocumentTypeRaw[Id] = JsonNode.Parse(
            """{ "alias": "blogPost", "name": "Blog post" }"""
        )!;

        var (exit, output) = await Run(fake, "document-type create --example");

        Assert.Equal(0, exit);
        Assert.Contains("blogPost", output);
    }

    // ── get prints the raw body ───────────────────────────────────────────────

    [Theory]
    [InlineData("document-type", EntityKind.DocumentType)]
    [InlineData("data-type", EntityKind.DataType)]
    [InlineData("media-type", EntityKind.MediaType)]
    [InlineData("member-type", EntityKind.MemberType)]
    [InlineData("template", EntityKind.Template)]
    public async Task Get_ByReference_PrintsTheRawBody(string noun, EntityKind kind)
    {
        var fake = new FakeUmbracoManagementClient();
        fake.References[(kind, "thing")] = Id;
        var store = kind switch
        {
            EntityKind.DocumentType => fake.DocumentTypeRaw,
            EntityKind.DataType => fake.DataTypeRaw,
            EntityKind.MediaType => fake.MediaTypeRaw,
            EntityKind.MemberType => fake.MemberTypeRaw,
            _ => fake.TemplateRaw,
        };
        store[Id] = JsonNode.Parse("""{ "alias": "thing", "rawOnly": { "kept": true } }""")!;

        var (exit, output) = await Run(fake, $"{noun} get thing");

        Assert.Equal(0, exit);
        Assert.True(JsonNode.Parse(output)!["data"]!["rawOnly"]!["kept"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Get_UnknownReference_FailsWithoutReading()
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, "media-type get nope");

        Assert.NotEqual(0, exit);
    }

    // ── update --json-body merges, --replace replaces ─────────────────────────

    [Theory]
    [InlineData("document-type", EntityKind.DocumentType)]
    [InlineData("data-type", EntityKind.DataType)]
    [InlineData("media-type", EntityKind.MediaType)]
    [InlineData("member-type", EntityKind.MemberType)]
    [InlineData("template", EntityKind.Template)]
    public async Task Update_JsonBody_MergesIntoTheResolvedItem(string noun, EntityKind kind)
    {
        var fake = new FakeUmbracoManagementClient();
        fake.References[(kind, "thing")] = Id;

        var (exit, _) = await Run(
            fake,
            $"{noun} update thing --json-body {{body}}",
            """{ "name": "N" }"""
        );

        Assert.Equal(0, exit);
        Assert.Equal(
            (kind, Id, WriteMode.Merge),
            (
                fake.LastSchemaMerge!.Value.Kind,
                fake.LastSchemaMerge.Value.Id,
                fake.LastSchemaMerge.Value.Mode
            )
        );
    }

    [Fact]
    public async Task Update_Replace_AsksForAReplace()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DocumentType, "blog")] = Id;

        await Run(
            fake,
            "document-type update blog --json-body {body} --replace --yes",
            """{ "name": "N" }"""
        );

        Assert.Equal(WriteMode.Replace, fake.LastSchemaMerge!.Value.Mode);
    }

    [Fact]
    public async Task MediaTypesUpdate_WithoutABody_IsAParseError()
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, "media-type update brochure");

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastSchemaMerge);
    }

    [Fact]
    public async Task MemberTypesUpdate_FlagsOnly_StillUsesTheScalarUpdate()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.MemberTypeRaw[Id] = JsonNode.Parse("""{ "alias": "member" }""")!;
        fake.References[(EntityKind.MemberType, "siteMember")] = Id;

        var (exit, _) = await Run(fake, "member-type update siteMember --name Author");

        Assert.Equal(0, exit);
        Assert.Equal(
            (Id, "Author"),
            (fake.LastMemberTypeUpdate!.Value.Id, fake.LastMemberTypeUpdate.Value.Request.Name)
        );
        Assert.Null(fake.LastSchemaMerge);
    }

    // ── create --json-body on media and member types ──────────────────────────

    [Theory]
    [InlineData("media-type", "mediaType")]
    [InlineData("member-type", "memberType")]
    public async Task Create_JsonBody_PostsTheRawBodyWithItsId(string noun, string rawKind)
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(
            fake,
            $"{noun} create --json-body {{body}} --id {Id}",
            """{ "alias": "brochure", "name": "Brochure" }"""
        );

        Assert.Equal(0, exit);
        var write = Assert.Single(fake.RawWrites);
        Assert.Equal((rawKind, Id.ToString()), (write.Kind, write.Body["id"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("media-type")]
    [InlineData("member-type")]
    public async Task Create_NoFlagsAndNoBody_IsAParseError(string noun)
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, $"{noun} create --name OnlyName");

        Assert.NotEqual(0, exit);
        Assert.Empty(fake.RawWrites);
    }
}
