using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Serialization behaviour of <see cref="SchemaSnapshot"/> (#68 / ADR 0005): it round-trips
/// through JSON preserving verbatim bodies, and <see cref="SchemaSnapshot.FromJson"/> tolerates
/// a CLI output envelope so a snapshot piped straight from <c>schema export</c> still loads.
/// </summary>
public class SchemaSnapshotTests
{
    [Fact]
    public void ToJson_FromJson_RoundTripsBodiesVerbatim()
    {
        var id = Guid.NewGuid();
        var snap = new SchemaSnapshot
        {
            DocumentTypes =
            {
                JsonNode.Parse(
                    $$"""{"id":"{{id}}","alias":"blogPost","properties":[{"alias":"body"}]}"""
                )!,
            },
        };

        var reloaded = SchemaSnapshot.FromJson(snap.ToJson());

        Assert.Equal("blogPost", (string?)reloaded.DocumentTypes.Single()["alias"]);
        // The rich collection the lossy record would drop survives the round trip.
        Assert.Equal("body", (string?)reloaded.DocumentTypes.Single()["properties"]![0]!["alias"]);
    }

    [Fact]
    public void FromJson_UnwrapsCliEnvelope()
    {
        // A user pipes `schema export` (which emits {status,data,meta}) straight into a file
        // and then `apply`s it; FromJson must unwrap the inner data object.
        var enveloped = """
            {"status":"success",
             "data":{"schemaVersion":"2","documentTypes":[{"alias":"home"}],"mediaTypes":[],"memberTypes":[],"dataTypes":[],"templates":[]},
             "meta":{"schemaVersion":"2"}}
            """;

        var snap = SchemaSnapshot.FromJson(enveloped);

        Assert.Equal("home", (string?)snap.DocumentTypes.Single()["alias"]);
    }

    /// <summary>
    /// A version-1 snapshot predates media types and member types (#186), so reading it would
    /// leave both arrays empty - and <c>apply --prune</c> reads an empty array as "the instance
    /// should have none of these", i.e. delete every media type and member type. Refusing the
    /// file and asking for a re-export is the only safe reading.
    /// </summary>
    [Fact]
    public void FromJson_Version1Snapshot_IsRefusedRatherThanReadAsEmpty()
    {
        var v1 = """
            {"schemaVersion":"1","documentTypes":[{"alias":"home"}],"dataTypes":[],"templates":[]}
            """;

        var error = Assert.ThrowsAny<JsonException>(() => SchemaSnapshot.FromJson(v1));

        Assert.Contains("'1'", error.Message);
        Assert.Contains("Re-export", error.Message);
    }

    [Fact]
    public void FromJson_Malformed_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => SchemaSnapshot.FromJson("not json"));
    }
}
