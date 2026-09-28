using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Blast-radius repro for the Phase 5 consolidation: <c>schema export</c> and <c>schema apply</c>
/// used to call fifteen per-kind raw methods and now call three kind-keyed ones. These pin, for
/// every kind, that the wire is what it was: the same endpoint, the body sent byte-for-byte, and
/// an apply update that is a plain PUT (no read, no merge) - so a snapshot still replaces the item
/// exactly as it did.
/// </summary>
public class SchemaRawPathWireTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private const string Body = """
        { "id": "__ID__", "alias": "a", "name": "A", "properties": [ { "alias": "p", "validation": { "mandatory": true } } ] }
        """;

    /// <summary>The body with the fixed id, parsed.</summary>
    /// <returns>A fresh body.</returns>
    private static JsonNode NewBody() => JsonNode.Parse(Body.Replace("__ID__", Id.ToString()))!;

    public static TheoryData<EntityKind, string> Kinds =>
        new()
        {
            { EntityKind.DocumentType, "document-type" },
            { EntityKind.DataType, "data-type" },
            { EntityKind.Template, "template" },
            { EntityKind.MediaType, "media-type" },
            { EntityKind.MemberType, "member-type" },
            // #227: the snapshot's GUID-keyed newcomers ride the same path.
            { EntityKind.DictionaryItem, "dictionary" },
            { EntityKind.MemberGroup, "member-group" },
            { EntityKind.UserGroup, "user-group" },
        };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task MergeSchemaItemAsync_Replace_PutsTheBodyVerbatimWithoutReading(
        EntityKind kind,
        string segment
    )
    {
        var handler = Wire.Blank();

        var result = await Wire.Client(handler)
            .MergeSchemaItemAsync(kind, Id, NewBody(), WriteMode.Replace, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(
            JsonNode.DeepEquals(
                NewBody(),
                handler.BodyOf(HttpMethod.Put, $"/umbraco/management/api/v1/{segment}/{Id}")
            ),
            "An apply update must send the snapshot body unchanged."
        );
        Assert.DoesNotContain(handler.Recordings, r => r.Method == HttpMethod.Get);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task CreateSchemaRawAsync_PostsTheBodyVerbatimToTheCollection(
        EntityKind kind,
        string segment
    )
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).CreateSchemaRawAsync(kind, NewBody(), CancellationToken.None);

        Assert.True(
            JsonNode.DeepEquals(
                NewBody(),
                handler.BodyOf(HttpMethod.Post, $"/umbraco/management/api/v1/{segment}")
            )
        );
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task GetSchemaRawAsync_ReadsTheByIdEndpoint(EntityKind kind, string segment)
    {
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.OK,
            Body.Replace("__ID__", Id.ToString())
        );

        var result = await Wire.Client(handler).GetSchemaRawAsync(kind, Id, CancellationToken.None);

        Assert.True(JsonNode.DeepEquals(NewBody(), result.Data));
        handler.AssertRequested(HttpMethod.Get, $"/umbraco/management/api/v1/{segment}/{Id}");
    }
}
