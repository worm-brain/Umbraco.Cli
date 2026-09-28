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
/// <c>content create --example --document-type &lt;alias&gt;</c> (#174): a create body with an
/// example value per property, chosen by the property's editor.
/// </summary>
[Collection("ConsoleCapture")]
public class ContentExampleBodyTests
{
    private static readonly Guid TextBoxType = Guid.NewGuid();
    private static readonly Guid CustomType = Guid.NewGuid();
    private static readonly Guid TrueFalseType = Guid.NewGuid();
    private static readonly Guid SeoComposition = Guid.NewGuid();

    /// <summary>
    /// A fake with a <c>blogPost</c> type (a textbox <c>title</c> that varies, a custom-editor
    /// <c>widget</c>) composing <c>seo</c> (a true/false <c>hideFromSearch</c>).
    /// </summary>
    /// <param name="variesByCulture">Whether blogPost varies by culture.</param>
    /// <returns>The fake.</returns>
    private static FakeUmbracoManagementClient Seeded(bool variesByCulture = false)
    {
        var fake = new FakeUmbracoManagementClient();
        // The command resolves the alias to an id, then reads the type by id (#262).
        var blogPost = Guid.NewGuid();
        fake.References[(EntityKind.DocumentType, "blogPost")] = blogPost;
        fake.DataTypeList.Add(
            new DataTypeResponse { Id = TextBoxType, EditorAlias = "Umbraco.TextBox" }
        );
        fake.DataTypeList.Add(new DataTypeResponse { Id = CustomType, EditorAlias = "My.Custom" });
        fake.DataTypeList.Add(
            new DataTypeResponse { Id = TrueFalseType, EditorAlias = "Umbraco.TrueFalse" }
        );
        fake.DocumentTypeList.Add(
            new DocumentTypeResponse
            {
                Id = blogPost,
                Alias = "blogPost",
                Name = "Blog Post",
                VariesByCulture = variesByCulture,
                Compositions = [SeoComposition],
                Properties =
                [
                    new()
                    {
                        Alias = "widget",
                        DataType = CustomType,
                        SortOrder = 2,
                    },
                    new()
                    {
                        Alias = "title",
                        DataType = TextBoxType,
                        SortOrder = 1,
                        VariesByCulture = variesByCulture,
                    },
                ],
            }
        );
        fake.DocumentTypeList.Add(
            new DocumentTypeResponse
            {
                Id = SeoComposition,
                Alias = "seo",
                Properties = [new() { Alias = "hideFromSearch", DataType = TrueFalseType }],
            }
        );
        return fake;
    }

    /// <summary>Builds the example for <c>blogPost</c> and returns it.</summary>
    /// <param name="fake">The seeded fake.</param>
    /// <param name="culture">The --culture value.</param>
    /// <returns>The body.</returns>
    private static async Task<JsonNode> Example(
        FakeUmbracoManagementClient fake,
        string? culture = null
    )
    {
        var result = await ContentExampleBody.BuildAsync(
            fake,
            "blogPost",
            culture,
            null,
            CancellationToken.None
        );
        return result.Data!;
    }

    /// <summary>The values entry for a property alias.</summary>
    private static JsonNode Value(JsonNode body, string alias) =>
        body["values"]!.AsArray().Single(v => (string?)v!["alias"] == alias)!;

    [Fact]
    public async Task BuildAsync_OneValuePerProperty_IncludingCompositions()
    {
        var body = await Example(Seeded());

        Assert.Equal(
            ["title", "widget", "hideFromSearch"],
            body["values"]!.AsArray().Select(v => (string?)v!["alias"])
        );
    }

    [Fact]
    public async Task BuildAsync_KnownEditor_GetsItsExampleValue()
    {
        var body = await Example(Seeded());

        Assert.Equal("some text", (string?)Value(body, "title")["value"]);
    }

    [Fact]
    public async Task BuildAsync_UnknownEditor_GetsNullValue()
    {
        var body = await Example(Seeded());

        Assert.Null(Value(body, "widget")["value"]);
    }

    [Fact]
    public async Task BuildAsync_UnknownEditor_NamesItsEditorAlias()
    {
        var body = await Example(Seeded());

        Assert.Equal("My.Custom", (string?)Value(body, "widget")["editorAlias"]);
    }

    [Fact]
    public async Task BuildAsync_InvariantType_HasNoCultures()
    {
        var body = await Example(Seeded());

        Assert.Null(body["variants"]![0]!["culture"]);
    }

    [Fact]
    public async Task BuildAsync_VariantTypeWithoutCulture_UsesTheDefaultLanguage()
    {
        var fake = Seeded(variesByCulture: true);
        fake.LanguageList.Add(new LanguageResponse { IsoCode = "da-DK", IsDefault = true });

        var body = await Example(fake);

        Assert.Equal("da-DK", (string?)Value(body, "title")["culture"]);
    }

    [Fact]
    public async Task BuildAsync_VariantTypeWithCulture_UsesIt()
    {
        var body = await Example(Seeded(variesByCulture: true), culture: "en-GB");

        Assert.Equal("en-GB", (string?)body["variants"]![0]!["culture"]);
    }

    [Fact]
    public async Task BuildAsync_InvariantPropertyOnVariantType_HasNullCulture()
    {
        var body = await Example(Seeded(variesByCulture: true), culture: "en-GB");

        Assert.Null(Value(body, "widget")["culture"]);
    }

    [Fact]
    public async Task BuildAsync_MissingComposition_ReturnsTheReadFailure()
    {
        var fake = Seeded();
        fake.DocumentTypeList.RemoveAll(t => t.Id == SeoComposition);
        fake.MissingDocumentTypeIds.Add(SeoComposition);

        var result = await ContentExampleBody.BuildAsync(
            fake,
            "blogPost",
            null,
            null,
            CancellationToken.None
        );

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task BuildAsync_Output_IsAcceptedByContentCreate()
    {
        // editorAlias is information for the caller; the create body reader must ignore it, so
        // the example can be edited and fed straight back.
        var body = await Example(Seeded());

        var request = ContentCreateCommand.ReadCreateRequest(body.ToJsonString(), null);

        Assert.Equal(3, request.Values.Count());
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("content create --example")]
    [InlineData(
        "content create --example --document-type blogPost --parent 3f7a8b2e-1234-5678-abcd-ef0123456789"
    )]
    [InlineData("content create --example --document-type blogPost --json-body b.json")]
    public void Parse_ExampleWithMissingOrConflictingInput_IsAnError(string args)
    {
        Assert.NotEmpty(TestCliRoot.Build().Parse(args).Errors);
    }

    [Fact]
    public void Parse_ExampleWithDocumentType_NeedsNoName()
    {
        var errors = TestCliRoot
            .Build()
            .Parse("content create --example --document-type blogPost --culture en-US")
            .Errors;

        Assert.Empty(errors);
    }

    // ── End to end ───────────────────────────────────────────────────────────

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    [Fact]
    public async Task ContentCreateExample_PrintsTheBodyInTheEnvelope()
    {
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var clientFactory = new FakeClientFactory(Seeded());
        var config = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")
        );
        var auth = new UmbracoAuthService(stub);
        var executor = new CommandExecutor(
            new CommandContextFactory(
                config,
                auth,
                stub,
                global,
                clientFactory,
                new MutationInterceptState()
            ),
            new ConsoleConfirmationPrompt()
        );
        var root = CliRoot.Build(global, config, auth, executor, stub, clientFactory);
        var original = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await root.Parse(
                    "--host https://x --token t --output json content create --example --document-type blogPost"
                )
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(original);
        }

        var data = JsonDocument.Parse(sw.ToString()).RootElement.GetProperty("data");
        Assert.Equal("blogPost", data.GetProperty("contentType").GetProperty("alias").GetString());
    }

    // ── The docs table is the same table ─────────────────────────────────────

    [Fact]
    public void PropertyValueExamples_EveryEditor_IsInTheCommandsDocTable()
    {
        var docs = File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "docs", "commands.md"));

        var missing = PropertyValueExamples
            .ByEditorAlias.Where(e =>
                !docs.Contains($"| {e.Value.Editor} | `{e.Key}` | `{e.Value.Json}` |")
            )
            .Select(e => e.Key)
            .ToList();

        Assert.True(missing.Count == 0, "Not in docs/commands.md: " + string.Join(", ", missing));
    }

    [Fact]
    public void PropertyValueExamples_EveryExample_IsValidJson()
    {
        foreach (var example in PropertyValueExamples.ByEditorAlias.Values)
            JsonNode.Parse(example.Json);
    }

    [Fact]
    public void For_MediaPicker_FillsTheEntryKeyWithARealGuid()
    {
        // Umbraco answered 500 to the "<new guid>" placeholder, so the example could not be sent.
        var value = PropertyValueExamples.For("Umbraco.MediaPicker3")!;

        Assert.True(Guid.TryParse((string?)value[0]!["key"], out _));
    }

    [Fact]
    public void For_MediaPickerTwice_GivesEachEntryItsOwnKey()
    {
        var first = PropertyValueExamples.For("Umbraco.MediaPicker3")!;
        var second = PropertyValueExamples.For("Umbraco.MediaPicker3")!;

        Assert.NotEqual((string?)first[0]!["key"], (string?)second[0]!["key"]);
    }

    [Fact]
    public void For_MediaPicker_LeavesTheMediaIdForTheCallerToSupply()
    {
        // Only the caller knows which media item to pick, so that stays a visible placeholder.
        var value = PropertyValueExamples.For("Umbraco.MediaPicker3")!;

        Assert.Equal("<media id>", (string?)value[0]!["mediaKey"]);
    }

    private static readonly JsonSerializerOptions Unescaped = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void For_EveryKnownEditor_LeavesNoNewGuidToken()
    {
        var leftovers = PropertyValueExamples
            .ByEditorAlias.Keys.Where(alias =>
                PropertyValueExamples
                    .For(alias)!
                    // Unescaped, or "<" is written as < and the check could never match.
                    .ToJsonString(Unescaped)
                    .Contains(PropertyValueExamples.NewGuid, StringComparison.Ordinal)
            )
            .ToList();

        Assert.Empty(leftovers);
    }

    [Fact]
    public void For_UnknownOrNullEditor_IsNull()
    {
        Assert.Equal(
            (null, null),
            (PropertyValueExamples.For("My.Custom"), PropertyValueExamples.For(null))
        );
    }
}
