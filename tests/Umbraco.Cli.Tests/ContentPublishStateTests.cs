using System.Text.Json.Nodes;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Which cultures content apply publishes and unpublishes (#223) so the target's publish state
/// matches the snapshot's, and that a matched target needs nothing (so a second apply is a no-op).
/// </summary>
public class ContentPublishStateTests
{
    /// <summary>A document body whose variants are (culture, state) pairs; null culture = invariant.</summary>
    private static JsonNode Body(params (string? Culture, string State)[] variants) =>
        new JsonObject
        {
            ["variants"] = new JsonArray([
                .. variants.Select(v =>
                    (JsonNode)new JsonObject { ["culture"] = v.Culture, ["state"] = v.State }
                ),
            ]),
        };

    [Fact]
    public void ForCreate_PublishesOnlyTheCulturesTheSnapshotHasPublished()
    {
        var steps = ContentPublishState.ForCreate(
            Body(("en-US", "Published"), ("da-DK", "Draft"), ("de-DE", "PublishedPendingChanges"))
        );

        Assert.Equal(["en-US", "de-DE"], steps.Publish);
    }

    [Fact]
    public void ForCreate_InvariantPublishedDocument_PublishesTheNullCulture()
    {
        var steps = ContentPublishState.ForCreate(Body((null, "Published")));

        Assert.Equal([null], steps.Publish);
    }

    [Fact]
    public void ForCreate_DraftDocument_PublishesNothing()
    {
        var steps = ContentPublishState.ForCreate(Body((null, "Draft")));

        Assert.True(steps.IsEmpty);
    }

    [Fact]
    public void ForMatch_SameStateAndBody_NeedsNothing()
    {
        var steps = ContentPublishState.ForMatch(
            Body(("en-US", "Published"), ("da-DK", "Draft")),
            Body(("en-US", "Published"), ("da-DK", "Draft")),
            bodyChanged: false
        );

        Assert.True(steps.IsEmpty);
    }

    [Fact]
    public void ForMatch_PublishedInSnapshotDraftLive_PublishesIt()
    {
        var steps = ContentPublishState.ForMatch(
            Body(("en-US", "Published")),
            Body(("en-US", "Draft")),
            bodyChanged: false
        );

        Assert.Equal(["en-US"], steps.Publish);
    }

    [Fact]
    public void ForMatch_BodyUpdatedOnAPublishedCulture_RepublishesIt()
    {
        // The update leaves the live culture pending; publishing makes the edit visible.
        var steps = ContentPublishState.ForMatch(
            Body(("en-US", "Published")),
            Body(("en-US", "Published")),
            bodyChanged: true
        );

        Assert.Equal(["en-US"], steps.Publish);
    }

    [Fact]
    public void ForMatch_PublishedLiveButNotInSnapshot_UnpublishesIt()
    {
        var steps = ContentPublishState.ForMatch(
            Body(("en-US", "Published"), ("da-DK", "Draft")),
            Body(("en-US", "Published"), ("da-DK", "Published")),
            bodyChanged: false
        );

        Assert.Equal(["da-DK"], steps.Unpublish);
    }

    [Fact]
    public void ForMatch_FullyPublishedSnapshotOverPendingTarget_PublishesIt()
    {
        var steps = ContentPublishState.ForMatch(
            Body(("en-US", "Published")),
            Body(("en-US", "PublishedPendingChanges")),
            bodyChanged: false
        );

        Assert.Equal(["en-US"], steps.Publish);
    }

    [Fact]
    public void ForMatch_PendingSnapshotOverPublishedTarget_LeavesItAlone()
    {
        // The snapshot has no copy of the source's published version, so there is nothing more
        // correct to publish, and a second apply must not keep republishing.
        var steps = ContentPublishState.ForMatch(
            Body(("en-US", "PublishedPendingChanges")),
            Body(("en-US", "Published")),
            bodyChanged: false
        );

        Assert.True(steps.IsEmpty);
    }
}
