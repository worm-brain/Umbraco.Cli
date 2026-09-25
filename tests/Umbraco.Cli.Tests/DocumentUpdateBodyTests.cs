using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The merge rules behind <c>content update</c> (#178/#179), tested directly rather than through
/// an <see cref="HttpClient"/>. <see cref="ContentUpdateMergeClientTests"/> covers the same rules
/// end to end over the wire; these cover the edges cheaply.
/// </summary>
public class DocumentUpdateBodyTests
{
    /// <summary>A document with one culture, two values and a template.</summary>
    /// <returns>The document object.</returns>
    private static JsonObject Document() =>
        JsonNode
            .Parse(
                """
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "template": { "id": "22222222-2222-2222-2222-222222222222" },
                  "urls": [ { "culture": "en-US", "url": "/" } ],
                  "values": [
                    { "alias": "title", "culture": "en-US", "segment": null, "value": "Hello", "editorAlias": "Umbraco.TextBox" }
                  ],
                  "variants": [
                    { "culture": "en-US", "segment": null, "name": "Hello", "state": "Published" }
                  ]
                }
                """
            )!
            .AsObject();

    /// <summary>An invariant document: a single variant with no culture.</summary>
    /// <returns>The document object.</returns>
    private static JsonObject InvariantDocument() =>
        JsonNode
            .Parse(
                """
                {
                  "values": [ { "alias": "title", "culture": null, "segment": null, "value": "Hello" } ],
                  "variants": [ { "culture": null, "segment": null, "name": "Home" } ]
                }
                """
            )!
            .AsObject();

    [Fact]
    public void Merge_ValueForANewAlias_IsAppended()
    {
        var document = Document();

        DocumentUpdateBody.Merge(
            document,
            [
                new ContentValue
                {
                    Alias = "body",
                    Culture = "en-US",
                    Value = "Text",
                },
            ],
            []
        );

        Assert.Equal(2, document["values"]!.AsArray().Count);
    }

    [Fact]
    public void Merge_ValueForTheSameKey_ReplacesInPlace()
    {
        var document = Document();

        DocumentUpdateBody.Merge(
            document,
            [
                new ContentValue
                {
                    Alias = "title",
                    Culture = "en-US",
                    Value = "Changed",
                },
            ],
            []
        );

        var value = Assert.Single(document["values"]!.AsArray());
        Assert.Equal("Changed", value!["value"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_UntouchedFields_AreLeftAlone()
    {
        var document = Document();

        DocumentUpdateBody.Merge(document, [], []);

        Assert.Equal(
            "22222222-2222-2222-2222-222222222222",
            document["template"]!["id"]!.GetValue<string>()
        );
        Assert.NotNull(document["urls"]);
    }

    [Fact]
    public void Merge_EmptyRequest_KeepsEveryValueAndVariant()
    {
        // `content update <id> --template x` sends an empty request (#208), so an empty merge
        // must change nothing but what the caller named.
        var document = Document();

        DocumentUpdateBody.Merge(document, [], []);

        Assert.Equal(
            "Hello",
            Assert.Single(document["values"]!.AsArray())!["value"]!.GetValue<string>()
        );
        Assert.Equal(
            "Hello",
            Assert.Single(document["variants"]!.AsArray())!["name"]!.GetValue<string>()
        );
    }

    [Fact]
    public void Merge_ResponseOnlyFields_AreNotEchoedBack()
    {
        var document = Document();

        DocumentUpdateBody.Merge(document, [], []);

        // editorAlias and state exist on the response models but not the request ones, and every
        // entry is projected to the request shape whether it was carried over or supplied.
        Assert.Null(document["values"]![0]!["editorAlias"]);
        Assert.Null(document["variants"]![0]!["state"]);
    }

    [Fact]
    public void Merge_Replace_DropsUnlistedValuesAndVariantsButKeepsTheTemplate()
    {
        var document = Document();

        DocumentUpdateBody.Merge(
            document,
            [
                new ContentValue
                {
                    Alias = "body",
                    Culture = "en-US",
                    Value = "Only",
                },
            ],
            [new ContentVariant { Culture = "en-US", Name = "Only" }],
            replace: true
        );

        var value = Assert.Single(document["values"]!.AsArray());
        Assert.Equal("body", value!["alias"]!.GetValue<string>());
        Assert.Equal(
            "22222222-2222-2222-2222-222222222222",
            document["template"]!["id"]!.GetValue<string>()
        );
    }

    [Fact]
    public void Merge_VariantWithNoCultureOnAVaryingDocument_Throws()
    {
        var document = Document();

        var ex = Assert.Throws<ApiException>(() =>
            DocumentUpdateBody.Merge(document, [], [new ContentVariant { Name = "Renamed" }])
        );

        Assert.Contains("varies by culture", ex.Message);
        Assert.Contains("en-US", ex.Message);
    }

    [Fact]
    public void Merge_VariantWithNoCultureOnAnInvariantDocument_IsAccepted()
    {
        var document = InvariantDocument();

        DocumentUpdateBody.Merge(document, [], [new ContentVariant { Name = "Renamed" }]);

        var variant = Assert.Single(document["variants"]!.AsArray());
        Assert.Equal("Renamed", variant!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Merge_InvariantPropertyOnAVaryingDocument_IsStillAllowed()
    {
        var document = Document();

        // A property that does not vary carries a null culture even when the document varies, so
        // the variant guard must not apply to values.
        DocumentUpdateBody.Merge(
            document,
            [
                new ContentValue
                {
                    Alias = "sitewideNote",
                    Culture = null,
                    Value = "Shared",
                },
            ],
            []
        );

        Assert.Contains(
            document["values"]!.AsArray(),
            v => v!["alias"]!.GetValue<string>() == "sitewideNote"
        );
    }

    [Fact]
    public void Merge_DocumentWithNoValuesArray_StillMerges()
    {
        var document = new JsonObject();

        DocumentUpdateBody.Merge(
            document,
            [new ContentValue { Alias = "title", Value = "Hello" }],
            [new ContentVariant { Name = "Hello" }]
        );

        Assert.Single(document["values"]!.AsArray());
        Assert.Single(document["variants"]!.AsArray());
    }
}
