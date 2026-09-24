using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of <see cref="SchemaApplier"/> (#68 / ADR 0005 §4): dependency-ordered writes,
/// prune gating, the dry-run "write nothing" guarantee, id-rewrite on alias-matched updates, and
/// fail-fast. Driven through the fake, asserting the exact writes recorded.
/// </summary>
public class SchemaApplierTests
{
    private static JsonNode Doc(Guid id, string alias, params Guid[] compositions)
    {
        var comps = new JsonArray();
        foreach (var c in compositions)
            comps.Add(
                new JsonObject { ["documentType"] = new JsonObject { ["id"] = c.ToString() } }
            );
        return new JsonObject
        {
            ["id"] = id.ToString(),
            ["alias"] = alias,
            ["compositions"] = comps,
        };
    }

    private static SchemaEntityChange Added(string kind, Guid id, JsonNode body) =>
        new(kind, SchemaChangeKind.Added, "x", id, null) { DesiredBody = body };

    private static SchemaDiff DiffWith(
        SchemaKindDiff? docs = null,
        SchemaKindDiff? data = null,
        SchemaKindDiff? mediaTypes = null,
        SchemaKindDiff? memberTypes = null
    ) =>
        new(
            docs ?? Empty(),
            mediaTypes ?? Empty(),
            memberTypes ?? Empty(),
            data ?? Empty(),
            Empty()
        );

    private static SchemaKindDiff Empty() => new([], [], [], [], 0);

    [Fact]
    public async Task ApplyAsync_DryRun_WritesNothing_ButPlansEverything()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = DiffWith(
            docs: new SchemaKindDiff(
                Added: [Added("documentType", Guid.NewGuid(), Doc(Guid.NewGuid(), "a"))],
                Changed: [],
                Removed: [],
                Skipped: [],
                Unchanged: 0
            )
        );

        var result = await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: true,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.DryRun);
        Assert.Equal(1, result.Data.Created);
        Assert.Empty(fake.RawWrites); // the whole point of dry-run: nothing was sent
    }

    [Fact]
    public async Task ApplyAsync_CreatesInDependencyOrder_DataTypesThenDocTypes()
    {
        // A data type create and a doc type create: data types must be written first because a
        // doc type's properties reference data types.
        var fake = new FakeUmbracoManagementClient();
        var dataId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var diff = DiffWith(
            docs: new SchemaKindDiff(
                [Added("documentType", docId, Doc(docId, "blogPost"))],
                [],
                [],
                [],
                0
            ),
            data: new SchemaKindDiff(
                [Added("dataType", dataId, new JsonObject { ["id"] = dataId.ToString() })],
                [],
                [],
                [],
                0
            )
        );

        await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        Assert.Equal(2, fake.RawWrites.Count);
        Assert.Equal("dataType", fake.RawWrites[0].Kind);
        Assert.Equal("documentType", fake.RawWrites[1].Kind);
    }

    [Fact]
    public async Task ApplyAsync_TopologicallyOrdersCompositions()
    {
        // Doc type "child" composes "base"; base must be created before child.
        var fake = new FakeUmbracoManagementClient();
        var baseId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var diff = DiffWith(
            docs: new SchemaKindDiff(
                // Deliberately list child first to prove the sort reorders it after base.
                [
                    Added("documentType", childId, Doc(childId, "child", baseId)),
                    Added("documentType", baseId, Doc(baseId, "base")),
                ],
                [],
                [],
                [],
                0
            )
        );

        await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        var order = fake.RawWrites.Select(w => (string?)w.Body["alias"]).ToList();
        Assert.Equal(new[] { "base", "child" }, order);
    }

    [Fact]
    public async Task ApplyAsync_WithoutPrune_DoesNotDelete()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = DiffWith(
            docs: new SchemaKindDiff(
                [],
                [],
                Removed:
                [
                    new SchemaEntityChange(
                        "documentType",
                        SchemaChangeKind.Removed,
                        "old",
                        null,
                        Guid.NewGuid()
                    ),
                ],
                [],
                0
            )
        );

        var result = await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(fake.SchemaDeletedIds); // prune off -> the Removed entity is left alone
        Assert.Equal(0, result.Data!.Deleted);
    }

    [Fact]
    public async Task ApplyAsync_WithPrune_DeletesRemovedEntities()
    {
        var fake = new FakeUmbracoManagementClient();
        var removedId = Guid.NewGuid();
        var diff = DiffWith(
            docs: new SchemaKindDiff(
                [],
                [],
                Removed:
                [
                    new SchemaEntityChange(
                        "documentType",
                        SchemaChangeKind.Removed,
                        "old",
                        null,
                        removedId
                    ),
                ],
                [],
                0
            )
        );

        var result = await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: true,
            dryRun: false,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(removedId, Assert.Single(fake.SchemaDeletedIds));
    }

    [Fact]
    public async Task ApplyAsync_UpdateRewritesBodyIdToLiveId()
    {
        // Alias-matched change: snapshot id differs from the live id. The update must target the
        // live id AND send it in the body (Umbraco ids are immutable).
        var fake = new FakeUmbracoManagementClient();
        var snapshotId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        var change = new SchemaEntityChange(
            "template",
            SchemaChangeKind.Changed,
            "home",
            snapshotId,
            liveId,
            IdMismatch: true
        )
        {
            DesiredBody = new JsonObject { ["id"] = snapshotId.ToString(), ["alias"] = "home" },
        };
        var diff = new SchemaDiff(
            Empty(),
            Empty(),
            Empty(),
            Empty(),
            new SchemaKindDiff([], [change], [], [], 0)
        );

        await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        var write = Assert.Single(fake.RawWrites);
        Assert.Equal(liveId, write.Id); // targeted the live id
        Assert.Equal(liveId.ToString(), (string?)write.Body["id"]); // body id rewritten to live id
    }

    [Fact]
    public async Task ApplyAsync_FailFast_StopsAtFirstError()
    {
        var fake = new FakeUmbracoManagementClient
        {
            RawWriteFailure = UmbracoResponse<Empty>.Failure(400, "bad request"),
        };
        var diff = DiffWith(
            data: new SchemaKindDiff(
                [
                    Added(
                        "dataType",
                        Guid.NewGuid(),
                        new JsonObject { ["id"] = Guid.NewGuid().ToString() }
                    ),
                    Added(
                        "dataType",
                        Guid.NewGuid(),
                        new JsonObject { ["id"] = Guid.NewGuid().ToString() }
                    ),
                ],
                [],
                [],
                [],
                0
            )
        );

        var result = await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Single(fake.RawWrites); // stopped after the first failed write, did not attempt the second
    }

    /// <summary>
    /// Media and member types are created after data types and templates but before document
    /// types (#186): their properties reference data types, and a document type can allow a
    /// media type as a picker's start node, so the referenced entity has to exist first.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_CreatesMediaAndMemberTypesBetweenTemplatesAndDocumentTypes()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = DiffWith(
            docs: new SchemaKindDiff(
                [Added(SchemaKinds.DocumentType, Guid.NewGuid(), Doc(Guid.NewGuid(), "blogPost"))],
                [],
                [],
                [],
                0
            ),
            data: new SchemaKindDiff(
                [Added(SchemaKinds.DataType, Guid.NewGuid(), Doc(Guid.NewGuid(), "slider"))],
                [],
                [],
                [],
                0
            ),
            mediaTypes: new SchemaKindDiff(
                [Added(SchemaKinds.MediaType, Guid.NewGuid(), Doc(Guid.NewGuid(), "customImage"))],
                [],
                [],
                [],
                0
            ),
            memberTypes: new SchemaKindDiff(
                [Added(SchemaKinds.MemberType, Guid.NewGuid(), Doc(Guid.NewGuid(), "subscriber"))],
                [],
                [],
                [],
                0
            )
        );

        await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        Assert.Equal(
            ["dataType", "mediaType", "memberType", "documentType"],
            fake.RawWrites.Select(w => w.Kind)
        );
    }

    /// <summary>
    /// And prune deletes in the reverse order, so a document type that allows a media type goes
    /// before the media type it depends on.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_Prune_DeletesDocumentTypesBeforeMemberAndMediaTypes()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = DiffWith(
            docs: new SchemaKindDiff([], [], [Removed(SchemaKinds.DocumentType)], [], 0),
            mediaTypes: new SchemaKindDiff([], [], [Removed(SchemaKinds.MediaType)], [], 0),
            memberTypes: new SchemaKindDiff([], [], [Removed(SchemaKinds.MemberType)], [], 0)
        );

        var result = await SchemaApplier.ApplyAsync(
            fake,
            diff,
            prune: true,
            dryRun: true,
            CancellationToken.None
        );

        Assert.Equal(
            [SchemaKinds.DocumentType, SchemaKinds.MemberType, SchemaKinds.MediaType],
            result.Data!.Actions.Select(a => a.Kind)
        );
    }

    /// <summary>A prune candidate of one kind, with a live id to delete.</summary>
    /// <param name="kind">The entity kind.</param>
    /// <returns>The change.</returns>
    private static SchemaEntityChange Removed(string kind) =>
        new(kind, SchemaChangeKind.Removed, "x", null, Guid.NewGuid());
}
