using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the content write paths actually put on the wire (#187 Phase 2).
/// <para>
/// Update and publish are covered by <see cref="ContentUpdateMergeClientTests"/> and
/// <see cref="ContentPublishWireTests"/>, which landed with the Phase 1 fix. This file covers the
/// rest of the noun, concentrating on the shapes that hid #158 and #178: an optional nested object
/// that may be omitted, a collection that may be empty, and a sentinel culture value.
/// </para>
/// </summary>
public class ContentWireTests
{
    // ── create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateContentAsync_SendsIdDocumentTypeVariantsAndValues()
    {
        var docTypeId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateContentAsync(
                new CreateContentRequest
                {
                    Id = id,
                    ContentType = new ContentTypeReference { Id = docTypeId },
                    Variants = [new ContentVariant { Name = "About" }],
                    Values = [new ContentValue { Alias = "title", Value = "About us" }],
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/document");
        Assert.Equal(id.ToString(), body["id"]!.GetValue<string>());
        Assert.Equal(docTypeId.ToString(), body["documentType"]!["id"]!.GetValue<string>());
        Assert.Equal("About", body["variants"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("title", body["values"]![0]!["alias"]!.GetValue<string>());
        Assert.Equal("About us", body["values"]![0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateContentAsync_NoTemplate_StillWritesTheTemplateKeyAsNull()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateContentAsync(
                new CreateContentRequest
                {
                    ContentType = new ContentTypeReference { Id = Guid.NewGuid() },
                    Variants = [new ContentVariant { Name = "About" }],
                },
                CancellationToken.None
            );

        // #134: Umbraco 17 marks template required on a create body and reads an explicit null as
        // "the document type's default". Kiota omits null complex properties, so the key is forced.
        var body = handler.BodyOf(HttpMethod.Post, "/document");
        Assert.True(body.ContainsKey("template"));
        Assert.Null(body["template"]);
    }

    [Fact]
    public async Task CreateContentAsync_WithTemplateId_SendsIt()
    {
        var templateId = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateContentAsync(
                new CreateContentRequest
                {
                    ContentType = new ContentTypeReference { Id = Guid.NewGuid() },
                    Variants = [new ContentVariant { Name = "Post" }],
                    Template = new ContentTemplateReference { Id = templateId },
                },
                CancellationToken.None
            );

        Assert.Equal(
            templateId.ToString(),
            handler.BodyOf(HttpMethod.Post, "/document")["template"]!["id"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task CreateContentAsync_NoParent_OmitsTheParentRatherThanSendingANullId()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateContentAsync(
                new CreateContentRequest
                {
                    ContentType = new ContentTypeReference { Id = Guid.NewGuid() },
                    Variants = [new ContentVariant { Name = "Root" }],
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/document");
        Assert.False(
            body.ContainsKey("parent"),
            "A root-level create must omit parent entirely, not send {\"parent\":{\"id\":null}}."
        );
    }

    // ── unpublish ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnpublishContentAsync_NoCultures_OmitsTheCulturesField()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).UnpublishContentAsync(id, ct: CancellationToken.None);

        // #149: omitting the field is how the whole document is unpublished. Sending ["*"] here is
        // rejected on invariant content, because "*" is the invariant culture, not a wildcard.
        var body = handler.BodyOf(HttpMethod.Put, $"/document/{id}/unpublish");
        Assert.False(body.ContainsKey("cultures"));
    }

    [Fact]
    public async Task UnpublishContentAsync_WithCultures_SendsExactlyThose()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).UnpublishContentAsync(id, ["da-DK"], CancellationToken.None);

        var cultures = handler.BodyOf(HttpMethod.Put, $"/document/{id}/unpublish")[
            "cultures"
        ]!.AsArray();
        Assert.Equal("da-DK", Assert.Single(cultures)!.GetValue<string>());
    }

    // ── publish with descendants ──────────────────────────────────────────────

    [Fact]
    public async Task PublishContentWithDescendantsAsync_NoCultures_SendsTheStarSentinel()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .PublishContentWithDescendantsAsync(id, ct: CancellationToken.None);

        // Deliberately different from `content publish`, which enumerates the document's cultures
        // because "*" is the invariant culture there and 400s on a varying document (#158). This
        // endpoint DOES accept "*", verified against 17.7.0 on both invariant and variant
        // documents during the 2026-09-23 round. Pinned so nobody "fixes" it by analogy.
        var cultures = handler.BodyOf(HttpMethod.Put, $"/document/{id}/publish-with-descendants")[
            "cultures"
        ]!.AsArray();
        Assert.Equal("*", Assert.Single(cultures)!.GetValue<string>());
    }

    [Fact]
    public async Task PublishContentWithDescendantsAsync_SendsTheIncludeUnpublishedFlag()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .PublishContentWithDescendantsAsync(
                id,
                ["en-US"],
                includeUnpublishedDescendants: true,
                ct: CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, $"/document/{id}/publish-with-descendants");
        Assert.True(body["includeUnpublishedDescendants"]!.GetValue<bool>());
        Assert.Equal("en-US", Assert.Single(body["cultures"]!.AsArray())!.GetValue<string>());
    }

    // ── move / restore / copy: the null-means-root shapes ─────────────────────

    [Fact]
    public async Task MoveContentAsync_WithParent_SendsTheTarget()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).MoveContentAsync(id, parent, CancellationToken.None);

        Assert.Equal(
            parent.ToString(),
            handler.BodyOf(HttpMethod.Put, $"/document/{id}/move")["target"]![
                "id"
            ]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task MoveContentAsync_NoParent_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).MoveContentAsync(id, null, CancellationToken.None);

        // Moving to the content root must OMIT target - not send {"target":{"id":null}}. Absent
        // and explicitly-null are different bytes, and only one of them means "the root".
        Assert.False(handler.BodyOf(HttpMethod.Put, $"/document/{id}/move").ContainsKey("target"));
        // ...and it must not fall back to a second call against the root.
        handler.AssertNoRequest(HttpMethod.Put, "/document/move");
    }

    [Fact]
    public async Task RestoreContentAsync_WithParent_SendsTheTarget()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).RestoreContentAsync(id, parent, ct: CancellationToken.None);

        Assert.Equal(
            parent.ToString(),
            handler.BodyOf(HttpMethod.Put, $"/recycle-bin/document/{id}/restore")["target"]![
                "id"
            ]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task RestoreContentAsync_ToRoot_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .RestoreContentAsync(id, null, toRoot: true, ct: CancellationToken.None);

        Assert.False(
            handler
                .BodyOf(HttpMethod.Put, $"/recycle-bin/document/{id}/restore")
                .ContainsKey("target")
        );
    }

    [Fact]
    public async Task RestoreContentAsync_NoParent_RestoresUnderTheOriginalParent()
    {
        var id = Guid.NewGuid();
        var original = Guid.NewGuid();
        var handler = Wire.Routed(("/original-parent", $$"""{ "id": "{{original}}" }"""));

        await Wire.Client(handler).RestoreContentAsync(id, ct: CancellationToken.None);

        Assert.Equal(
            original.ToString(),
            handler.BodyOf(HttpMethod.Put, $"/recycle-bin/document/{id}/restore")["target"]![
                "id"
            ]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task RestoreContentAsync_NoParentAndOriginallyAtTheRoot_SendsNoTargetObject()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Routed(("/original-parent", "null"));

        await Wire.Client(handler).RestoreContentAsync(id, ct: CancellationToken.None);

        Assert.False(
            handler
                .BodyOf(HttpMethod.Put, $"/recycle-bin/document/{id}/restore")
                .ContainsKey("target")
        );
    }

    [Fact]
    public async Task RestoreContentAsync_WithParent_DoesNotAskForTheOriginalParent()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .RestoreContentAsync(id, Guid.NewGuid(), ct: CancellationToken.None);

        handler.AssertNoRequest(HttpMethod.Get, $"/recycle-bin/document/{id}/original-parent");
    }

    [Fact]
    public async Task RestoreContentAsync_RejectedAtTheTarget_SaysWhereItWasGoing()
    {
        var id = Guid.NewGuid();
        var original = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/original-parent"),
                HttpStatusCode.OK,
                $$"""{ "id": "{{original}}" }"""
            )
            .When(
                _ => true,
                HttpStatusCode.BadRequest,
                """{ "title": "The attempted operation was not permitted", "status": 400 }"""
            );

        var result = await Wire.Client(handler).RestoreContentAsync(id, ct: CancellationToken.None);

        Assert.Equal(
            $"Umbraco would not restore {id} under {original} (its original parent): "
                + "The attempted operation was not permitted. Its document type may not be allowed "
                + "there; pass --parent <id> to restore it somewhere else.",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task CopyContentAsync_SendsTargetAndBothFlags()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).CopyContentAsync(id, parent, true, true, CancellationToken.None);

        var body = handler.BodyOf(HttpMethod.Post, $"/document/{id}/copy");
        Assert.Equal(parent.ToString(), body["target"]!["id"]!.GetValue<string>());
        Assert.True(body["includeDescendants"]!.GetValue<bool>());
        Assert.True(body["relateToOriginal"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CopyContentAsync_DefaultFlags_SendsThemAsFalseNotOmitted()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).CopyContentAsync(id, null, ct: CancellationToken.None);

        // false and omitted are not the same to the server, so the flags must be present.
        var body = handler.BodyOf(HttpMethod.Post, $"/document/{id}/copy");
        Assert.False(body["includeDescendants"]!.GetValue<bool>());
        Assert.False(body["relateToOriginal"]!.GetValue<bool>());
    }

    // ── rollback: correctness lives entirely in a query parameter (#184 class) ─

    [Fact]
    public async Task RollbackDocumentVersionAsync_WithCulture_SendsItAsTheCultureParameter()
    {
        var versionId = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .RollbackDocumentVersionAsync(versionId, "da-DK", CancellationToken.None);

        Assert.Equal("da-DK", handler.QueryOf(HttpMethod.Post, "rollback")["culture"]);
    }

    [Fact]
    public async Task RollbackDocumentVersionAsync_NoCulture_SendsNoCultureParameter()
    {
        var versionId = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .RollbackDocumentVersionAsync(versionId, null, CancellationToken.None);

        Assert.Null(handler.QueryOf(HttpMethod.Post, "rollback")["culture"]);
    }

    // ── bodiless verbs: method and path are the whole contract ────────────────

    [Fact]
    public async Task TrashContentAsync_PutsToMoveToRecycleBin()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).TrashContentAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Put, $"/document/{id}/move-to-recycle-bin");
    }

    [Fact]
    public async Task DeleteContentAsync_DeletesTheDocument()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteContentAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/document/{id}");
    }

    [Fact]
    public async Task EmptyContentRecycleBinAsync_DeletesTheRecycleBinNotTheDocumentTree()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).EmptyContentRecycleBinAsync(CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, "/recycle-bin/document");
    }
}
