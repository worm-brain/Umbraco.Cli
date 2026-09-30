using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How the CLI reads what a site's packages declare about it (#440, ADR 0009): the
/// <c>umbracoCli</c> entries in each manifest, the vocabulary they are resolved against, and the
/// client's once-per-invocation read of the manifest endpoint.
/// </summary>
public class SiteCapabilitiesTests
{
    /// <summary>A manifest whose <c>umbracoCli</c> entry declares <paramref name="meta"/>.</summary>
    /// <param name="id">The package id.</param>
    /// <param name="meta">The declared meta, as JSON.</param>
    /// <returns>The manifest.</returns>
    private static ManifestResponse Declaring(string id, string meta) =>
        new()
        {
            Id = id,
            Name = id,
            CliCapabilities = JsonNode.Parse(meta)!.AsObject(),
        };

    // ── SiteCapabilities.From ────────────────────────────────────────────────

    [Fact]
    public void From_NoDeclarations_FormatIsText()
    {
        // Arrange
        ManifestResponse[] manifests = [new() { Id = "Plain.Package", Name = "Plain" }];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal("text", capabilities.DictionaryValueFormat);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("markdown")]
    [InlineData("text")]
    public void From_OnePackageDeclaresAFormat_FormatIsThatValue(string format)
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring("Rich.Package", $$"""{"dictionaryValueFormat":"{{format}}"}"""),
        ];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal(format, capabilities.DictionaryValueFormat);
    }

    [Fact]
    public void From_UnknownKey_IsIgnored()
    {
        // Arrange
        ManifestResponse[] manifests = [Declaring("Future.Package", """{"somethingNew":true}""")];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal("text", capabilities.DictionaryValueFormat);
    }

    [Theory]
    [InlineData("\"Markdown\"")] // the vocabulary is lowercase, matched exactly
    [InlineData("\"rtf\"")]
    [InlineData("42")]
    public void From_UnknownValue_IsIgnored(string value)
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring("Odd.Package", $$"""{"dictionaryValueFormat":{{value}}}"""),
        ];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal("text", capabilities.DictionaryValueFormat);
    }

    [Fact]
    public void From_TwoPackagesAgree_UsesTheValueWithoutAWarning()
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring("A.Package", """{"dictionaryValueFormat":"html"}"""),
            Declaring("B.Package", """{"dictionaryValueFormat":"html"}"""),
        ];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal(
            ("html", 0),
            (capabilities.DictionaryValueFormat, capabilities.Warnings.Count)
        );
    }

    [Fact]
    public void From_TwoPackagesDisagree_UsesTextAndWarnsNamingBoth()
    {
        // Arrange
        ManifestResponse[] manifests =
        [
            Declaring("A.Package", """{"dictionaryValueFormat":"html"}"""),
            Declaring("B.Package", """{"dictionaryValueFormat":"markdown"}"""),
        ];

        // Act
        var capabilities = SiteCapabilities.From(manifests);

        // Assert
        Assert.Equal(
            (
                "text",
                "packages disagree on dictionaryValueFormat (A.Package declares 'html', "
                    + "B.Package declares 'markdown'); using 'text'."
            ),
            (capabilities.DictionaryValueFormat, Assert.Single(capabilities.Warnings))
        );
    }

    [Fact]
    public void None_HasNoFormatAndSaysWhy()
    {
        // Act
        var capabilities = SiteCapabilities.None("forbidden");

        // Assert
        Assert.Equal(
            (null, "forbidden"),
            (capabilities.DictionaryValueFormat, capabilities.Unavailable)
        );
    }

    // ── SiteCapabilities.DeclaredIn ──────────────────────────────────────────

    [Fact]
    public void DeclaredIn_UmbracoCliEntries_MergesTheirMetaAndSkipsOtherTypes()
    {
        // Arrange: a backoffice extension (ignored) and two umbracoCli entries.
        var extensions = JsonNode.Parse(
            """
            [
              { "type": "workspaceView", "alias": "A.View", "meta": { "label": "x" } },
              { "type": "umbracoCli", "alias": "A.Cli", "meta": { "dictionaryValueFormat": "html" } },
              { "type": "umbracoCli", "alias": "A.Cli2", "meta": { "other": 1 } }
            ]
            """
        );

        // Act
        var meta = SiteCapabilities.DeclaredIn(extensions);

        // Assert
        Assert.Equal("""{"dictionaryValueFormat":"html","other":1}""", meta!.ToJsonString());
    }

    [Theory]
    [InlineData("""[{ "type": "workspaceView", "alias": "A.View" }]""")]
    [InlineData("""[{ "type": "umbracoCli", "alias": "A.Cli" }]""")] // no meta
    [InlineData("""{ "type": "umbracoCli" }""")] // not an array
    [InlineData("null")]
    public void DeclaredIn_NothingDeclared_IsNull(string extensions)
    {
        // Act
        var meta = SiteCapabilities.DeclaredIn(JsonNode.Parse(extensions));

        // Assert
        Assert.Null(meta);
    }

    // ── The client ───────────────────────────────────────────────────────────

    /// <summary>A real client over <paramref name="handler"/>.</summary>
    /// <param name="handler">Answers and records the requests.</param>
    /// <returns>The client.</returns>
    private static UmbracoManagementClient ClientOver(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://site.test/") });

    /// <summary>Whether a request is the manifest read.</summary>
    /// <param name="request">The request.</param>
    /// <returns>True for <c>GET .../manifest/manifest</c>.</returns>
    private static bool IsManifest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/manifest/manifest", StringComparison.Ordinal);

    [Fact]
    public async Task GetManifestsAsync_UmbracoCliExtension_MapsCliCapabilities()
    {
        // Arrange
        var handler = new RoutingHandler().When(
            IsManifest,
            HttpStatusCode.OK,
            """
            [{ "id": "Rich.Package", "name": "Rich", "version": "1.0.0", "extensions": [
                { "type": "umbracoCli", "alias": "Rich.Package.Cli", "meta": { "dictionaryValueFormat": "markdown" } }
            ] }]
            """
        );

        // Act
        var result = await ClientOver(handler).GetManifestsAsync();

        // Assert
        Assert.Equal(
            """{"dictionaryValueFormat":"markdown"}""",
            Assert.Single(result.Data!).CliCapabilities!.ToJsonString()
        );
    }

    [Fact]
    public async Task GetSiteCapabilitiesAsync_CalledTwice_ReadsTheManifestOnce()
    {
        // Arrange
        var handler = new RoutingHandler().When(IsManifest, HttpStatusCode.OK, "[]");
        var client = ClientOver(handler);

        // Act
        await client.GetSiteCapabilitiesAsync();
        await client.GetSiteCapabilitiesAsync();

        // Assert
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSiteCapabilitiesAsync_ManifestForbidden_ReturnsNoCapabilitiesWithAReason()
    {
        // Arrange
        var handler = new RoutingHandler().When(IsManifest, HttpStatusCode.Forbidden, "{}");

        // Act
        var capabilities = await ClientOver(handler).GetSiteCapabilitiesAsync();

        // Assert
        Assert.Equal(
            (null, true),
            (capabilities.DictionaryValueFormat, capabilities.Unavailable is not null)
        );
    }
}
