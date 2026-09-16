using System.Text.Json;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Parsing/serialization of a content snapshot (#100). The load path is the trust boundary for a
/// destructive <c>apply --prune</c>, so it must unwrap the CLI envelope, round-trip cleanly, and
/// reject anything that is not recognisably a snapshot (which would otherwise diff as "delete all").
/// </summary>
public class ContentSnapshotTests
{
    [Fact]
    public void FromJson_BareSnapshot_RoundTrips()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var json = $$"""
            {
              "contentVersion": "1",
              "root": "{{parent}}",
              "documents": [
                { "id": "{{id}}", "parent": "{{parent}}", "body": { "id": "{{id}}", "name": "A" } }
              ]
            }
            """;

        var snap = ContentSnapshot.FromJson(json);

        Assert.Equal(parent, snap.Root);
        var doc = Assert.Single(snap.Documents);
        Assert.Equal(id, doc.Id);
        Assert.Equal(parent, doc.Parent);
        Assert.Equal("A", (string?)doc.Body["name"]);

        // Serializing and re-parsing preserves the document.
        var reparsed = ContentSnapshot.FromJson(snap.ToJson());
        Assert.Equal(id, Assert.Single(reparsed.Documents).Id);
    }

    [Fact]
    public void FromJson_CliEnvelope_IsUnwrapped()
    {
        var id = Guid.NewGuid();
        var enveloped = $$"""
            {
              "status": "success",
              "data": { "contentVersion": "1", "documents": [ { "id": "{{id}}", "body": {} } ] },
              "meta": { "elapsedMs": 3 }
            }
            """;

        var snap = ContentSnapshot.FromJson(enveloped);

        Assert.Equal(id, Assert.Single(snap.Documents).Id);
    }

    [Fact]
    public void FromJson_EmptyObject_IsRejected()
    {
        // The prune footgun: {} would otherwise become an all-empty snapshot -> "delete everything".
        Assert.Throws<JsonException>(() => ContentSnapshot.FromJson("{}"));
    }

    [Fact]
    public void FromJson_UnknownVersion_IsRejected()
    {
        var json = """{ "contentVersion": "999", "documents": [] }""";

        Assert.Throws<JsonException>(() => ContentSnapshot.FromJson(json));
    }
}
