using System.Text.Json.Nodes;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The content body normaliser (#224): the same content on two instances compares equal once the
/// per-instance dates, flags and state are gone and the arrays are in a fixed order, while a real
/// edit still shows.
/// </summary>
public class ContentBodyNormaliserTests
{
    // The same Home document as exported from two instances: identical content, but every
    // instance-recorded field differs and the server listed values and variants in another order.
    private const string Source = """
        {
          "id": "0f0b1c7e-0000-0000-0000-000000000001",
          "documentType": { "id": "0f0b1c7e-0000-0000-0000-0000000000aa" },
          "template": null,
          "isTrashed": false,
          "flags": [],
          "values": [
            { "alias": "title", "culture": "en-US", "segment": null, "value": "Hello" },
            { "alias": "title", "culture": "da-DK", "segment": null, "value": "Hej" },
            { "alias": "body", "culture": null, "segment": null, "value": "<p>x</p>" }
          ],
          "variants": [
            { "culture": "en-US", "segment": null, "name": "Home", "state": "Published",
              "createDate": "2026-09-01T10:00:00Z", "updateDate": "2026-09-02T10:00:00Z",
              "publishDate": "2026-09-02T10:00:00Z", "scheduledPublishDate": null,
              "scheduledUnpublishDate": null, "flags": [] },
            { "culture": "da-DK", "segment": null, "name": "Hjem", "state": "Published",
              "createDate": "2026-09-01T10:00:00Z", "updateDate": "2026-09-02T10:00:00Z",
              "publishDate": "2026-09-02T10:00:00Z", "scheduledPublishDate": null,
              "scheduledUnpublishDate": null, "flags": [] }
          ]
        }
        """;

    private const string Target = """
        {
          "id": "0f0b1c7e-0000-0000-0000-000000000001",
          "documentType": { "id": "0f0b1c7e-0000-0000-0000-0000000000aa" },
          "template": null,
          "isTrashed": false,
          "flags": [ { "alias": "someServerFlag" } ],
          "values": [
            { "alias": "body", "culture": null, "segment": null, "value": "<p>x</p>" },
            { "alias": "title", "culture": "da-DK", "segment": null, "value": "Hej" },
            { "alias": "title", "culture": "en-US", "segment": null, "value": "Hello" }
          ],
          "variants": [
            { "culture": "da-DK", "segment": null, "name": "Hjem", "state": "Draft",
              "createDate": "2026-09-24T08:00:00Z", "updateDate": "2026-09-24T08:00:00Z",
              "publishDate": null, "scheduledPublishDate": null,
              "scheduledUnpublishDate": null, "flags": [] },
            { "culture": "en-US", "segment": null, "name": "Home", "state": "Draft",
              "createDate": "2026-09-24T08:00:00Z", "updateDate": "2026-09-24T08:00:00Z",
              "publishDate": null, "scheduledPublishDate": null,
              "scheduledUnpublishDate": null, "flags": [] }
          ]
        }
        """;

    [Fact]
    public void Normalise_SameContentFromTwoInstances_ComparesEqual()
    {
        var source = ContentBodyNormaliser.Normalise(JsonNode.Parse(Source)!);
        var target = ContentBodyNormaliser.Normalise(JsonNode.Parse(Target)!);

        Assert.True(JsonNode.DeepEquals(source, target), $"{source}\n---\n{target}");
    }

    [Fact]
    public void Normalise_ARealValueEdit_StillDiffers()
    {
        var edited = JsonNode.Parse(Target)!;
        edited["values"]![2]!["value"] = "Hello, world";

        var source = ContentBodyNormaliser.Normalise(JsonNode.Parse(Source)!);
        var target = ContentBodyNormaliser.Normalise(edited);

        Assert.False(JsonNode.DeepEquals(source, target));
    }

    [Fact]
    public void Normalise_StripsTheInstanceRecordedFields()
    {
        var normalised = ContentBodyNormaliser.Normalise(JsonNode.Parse(Source)!);

        var variant = normalised["variants"]![0]!.AsObject();
        Assert.Equal(["culture", "segment", "name"], variant.Select(p => p.Key).ToArray());
    }

    [Fact]
    public void Normalise_KeepsWhatACreateNeeds()
    {
        var normalised = ContentBodyNormaliser.Normalise(JsonNode.Parse(Source)!).AsObject();

        Assert.Equal(
            ["id", "documentType", "template", "values", "variants"],
            normalised.Select(p => p.Key).ToArray()
        );
    }

    [Fact]
    public void Normalise_DoesNotMutateTheInput()
    {
        var body = JsonNode.Parse(Source)!;

        ContentBodyNormaliser.Normalise(body);

        Assert.Equal("Published", (string?)body["variants"]![0]!["state"]);
    }

    [Fact]
    public void Normalise_NonObjectBody_IsReturnedUnchanged()
    {
        var normalised = ContentBodyNormaliser.Normalise(JsonNode.Parse("[1,2]")!);

        Assert.Equal("[1,2]", normalised.ToJsonString());
    }

    // ── #291: read-only editors ──────────────────────────────────────────────

    /// <summary>A body with a Label value (set by site code) and a text value.</summary>
    private static JsonNode LabelledBody(string submittedAt, string title) =>
        JsonNode.Parse(
            $$"""
            {"values":[
              {"editorAlias":"Umbraco.Label","alias":"submittedAt","culture":null,"segment":null,"value":"{{submittedAt}}"},
              {"editorAlias":"Umbraco.TextBox","alias":"title","culture":null,"segment":null,"value":"{{title}}"}
            ]}
            """
        )!;

    [Fact]
    public void ForComparison_LabelValuesThatDiffer_CompareEqual()
    {
        // Umbraco ignores a value sent for a Label, so the target keeps its own: a promotion can
        // never make them equal, and the diff must not report them.
        var source = ContentBodyNormaliser.ForComparison(LabelledBody("2026-09-01", "Hi"));
        var target = ContentBodyNormaliser.ForComparison(LabelledBody("2026-09-28", "Hi"));

        Assert.True(JsonNode.DeepEquals(source, target));
    }

    [Fact]
    public void ForComparison_OtherEditorValuesThatDiffer_StillDiffer()
    {
        var source = ContentBodyNormaliser.ForComparison(LabelledBody("2026-09-01", "Hi"));
        var target = ContentBodyNormaliser.ForComparison(LabelledBody("2026-09-01", "Hello"));

        Assert.False(JsonNode.DeepEquals(source, target));
    }

    [Fact]
    public void Normalise_KeepsLabelValues()
    {
        // Apply sends the normalised body, and must send what it always did.
        var normalised = ContentBodyNormaliser.Normalise(LabelledBody("2026-09-01", "Hi"));

        Assert.Equal(2, normalised["values"]!.AsArray().Count);
    }
}
