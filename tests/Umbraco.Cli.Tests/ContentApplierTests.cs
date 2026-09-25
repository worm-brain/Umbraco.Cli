using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Ordering and execution of a content apply (#100): creates run parent-first with the captured
/// parent injected, updates by id, prune only when asked and deepest-first, dry-run writes nothing,
/// and the run is fail-fast.
/// </summary>
public class ContentApplierTests
{
    private static ContentDocumentChange Added(Guid id, Guid? parent) =>
        new(ContentChangeKind.Added, id, parent)
        {
            DesiredBody = JsonNode.Parse($$"""{"id":"{{id}}","name":"X"}""")!,
        };

    private static ContentDocumentChange Changed(Guid id) =>
        new(ContentChangeKind.Changed, id)
        {
            DesiredBody = JsonNode.Parse($$"""{"id":"{{id}}","name":"Y"}""")!,
        };

    private static ContentDocumentChange Removed(Guid id) => new(ContentChangeKind.Removed, id);

    private static ContentDiff Diff(
        IReadOnlyList<ContentDocumentChange>? added = null,
        IReadOnlyList<ContentDocumentChange>? changed = null,
        IReadOnlyList<ContentDocumentChange>? removed = null
    ) => new(added ?? [], changed ?? [], removed ?? [], [], 0);

    [Fact]
    public async Task ApplyAsync_DryRun_WritesNothingAndPlansEveryStep()
    {
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        var diff = Diff(added: [Added(Guid.NewGuid(), null)], removed: [Removed(Guid.NewGuid())]);

        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: true,
            dryRun: true,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.DryRun);
        Assert.Empty(fake.RawWrites); // no create/update issued
        Assert.Empty(fake.CalledIds); // no delete issued
        Assert.All(result.Data!.Actions, a => Assert.Equal("planned", a.Status));
    }

    [Fact]
    public async Task ApplyAsync_Create_InjectsCapturedParentIntoBody()
    {
        var fake = new FakeUmbracoManagementClient();
        var parent = Guid.NewGuid();
        var diff = Diff(added: [Added(Guid.NewGuid(), parent)]);

        await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        var write = Assert.Single(fake.RawWrites);
        Assert.Null(write.Id); // create
        Assert.Equal(parent.ToString(), (string?)write.Body!["parent"]!["id"]);
    }

    [Fact]
    public async Task ApplyAsync_CreateAtRoot_SetsParentNull()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = Diff(added: [Added(Guid.NewGuid(), null)]);

        await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        var write = Assert.Single(fake.RawWrites);
        Assert.True(write.Body is JsonObject o && o["parent"] is null);
    }

    [Fact]
    public async Task ApplyAsync_PruneOff_IgnoresRemovals()
    {
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        var diff = Diff(removed: [Removed(Guid.NewGuid())]);

        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        Assert.Empty(fake.CalledIds);
        Assert.Equal(0, result.Data!.Deleted);
    }

    [Fact]
    public async Task ApplyAsync_Prune_DeletesDeepestFirst()
    {
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        // Removed list is in live pre-order (parents first); prune must delete in reverse.
        Guid a = Guid.NewGuid(),
            b = Guid.NewGuid(),
            c = Guid.NewGuid();
        var diff = Diff(removed: [Removed(a), Removed(b), Removed(c)]);

        await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: true,
            dryRun: false,
            CancellationToken.None
        );

        Assert.Equal(new[] { c, b, a }, fake.CalledIds);
    }

    // ── prune exclusions (#225) ───────────────────────────────────────────────
    // A staging site: Contact (in the snapshot) holds two form submissions created on staging,
    // and Blog (in the snapshot) holds a staging-only post. All three are prune candidates.

    private static readonly Guid Contact = Guid.NewGuid();
    private static readonly Guid Blog = Guid.NewGuid();
    private static readonly Guid Submission1 = Guid.NewGuid();
    private static readonly Guid Submission2 = Guid.NewGuid();
    private static readonly Guid StagingPost = Guid.NewGuid();
    private static readonly Guid SubmissionType = Guid.NewGuid();
    private static readonly Guid BlogPostType = Guid.NewGuid();

    private static ContentDocumentChange RemovedOfType(Guid id, Guid type) =>
        new(ContentChangeKind.Removed, id) { DocumentTypeId = type };

    private static ContentDiff StagingDiff() =>
        Diff(
            removed:
            [
                RemovedOfType(Submission1, SubmissionType),
                RemovedOfType(Submission2, SubmissionType),
                RemovedOfType(StagingPost, BlogPostType),
            ]
        ) with
        {
            LiveParents = new Dictionary<Guid, Guid?>
            {
                [Contact] = null,
                [Blog] = null,
                [Submission1] = Contact,
                [Submission2] = Contact,
                [StagingPost] = Blog,
            },
        };

    private static async Task<IReadOnlyList<Guid>> PrunedWith(
        ContentDiff diff,
        PruneExclusions exclude
    )
    {
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: true,
            dryRun: false,
            CancellationToken.None,
            exclude
        );
        return fake.CalledIds;
    }

    [Fact]
    public async Task ApplyAsync_PruneExcludingAType_KeepsEveryDocumentOfThatType()
    {
        var deleted = await PrunedWith(
            StagingDiff(),
            new PruneExclusions(new HashSet<Guid> { SubmissionType }, new HashSet<Guid>())
        );

        Assert.Equal([StagingPost], deleted);
    }

    [Fact]
    public async Task ApplyAsync_PruneExcludingARoot_KeepsItsSubtreeThroughAnAncestorInTheSnapshot()
    {
        // Contact itself is not a prune candidate; its children are found through the live
        // parent map, not the removed list.
        var deleted = await PrunedWith(
            StagingDiff(),
            new PruneExclusions(new HashSet<Guid>(), new HashSet<Guid> { Contact })
        );

        Assert.Equal([StagingPost], deleted);
    }

    [Fact]
    public async Task ApplyAsync_PruneOfAParentWithAnExcludedChild_KeepsTheParentToo()
    {
        // Deleting a document deletes its descendants, so a removed folder holding an excluded
        // submission must stay, or the delete would cascade onto the submission.
        var folder = Guid.NewGuid();
        var submission = Guid.NewGuid();
        var diff = Diff(
            removed:
            [
                RemovedOfType(folder, Guid.NewGuid()),
                RemovedOfType(submission, SubmissionType),
            ]
        ) with
        {
            LiveParents = new Dictionary<Guid, Guid?> { [folder] = null, [submission] = folder },
        };

        var deleted = await PrunedWith(
            diff,
            new PruneExclusions(new HashSet<Guid> { SubmissionType }, new HashSet<Guid>())
        );

        Assert.Empty(deleted);
    }

    [Fact]
    public async Task ApplyAsync_WriteFailure_StopsFailFast()
    {
        var fake = new FakeUmbracoManagementClient
        {
            RawWriteFailure = UmbracoResponse<Empty>.Failure(400, "bad"),
        };
        var diff = Diff(added: [Added(Guid.NewGuid(), null), Added(Guid.NewGuid(), null)]);

        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            prune: false,
            dryRun: false,
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Single(fake.RawWrites); // stopped after the first failing write
    }

    private static UmbracoResponse<Empty> Ok() => UmbracoResponse<Empty>.Success(Empty.Value);
}
