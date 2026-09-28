using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
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
        new(TreeChangeKind.Added, id, parent)
        {
            DesiredBody = JsonNode.Parse($$"""{"id":"{{id}}","name":"X"}""")!,
        };

    private static ContentDocumentChange Changed(Guid id) =>
        new(TreeChangeKind.Changed, id)
        {
            DesiredBody = JsonNode.Parse($$"""{"id":"{{id}}","name":"Y"}""")!,
            BodyChanged = true,
        };

    /// <summary>State steps that publish the named cultures.</summary>
    private static ContentPublishState.Steps Publishes(params string[] cultures) =>
        new(new PublishScope(cultures), null);

    /// <summary>State steps that unpublish the named cultures.</summary>
    private static ContentPublishState.Steps Unpublishes(params string[] cultures) =>
        new(null, new PublishScope(cultures));

    private static ContentDocumentChange Removed(Guid id) => new(TreeChangeKind.Removed, id);

    /// <summary>A diff whose documents are in the order given: added, then changed, then removed.</summary>
    private static ContentDiff Diff(
        IReadOnlyList<ContentDocumentChange>? added = null,
        IReadOnlyList<ContentDocumentChange>? changed = null,
        IReadOnlyList<ContentDocumentChange>? removed = null
    ) => new([.. added ?? [], .. changed ?? [], .. removed ?? []], 0);

    [Fact]
    public async Task ApplyAsync_DryRun_WritesNothingAndPlansEveryStep()
    {
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        var diff = Diff(added: [Added(Guid.NewGuid(), null)], removed: [Removed(Guid.NewGuid())]);

        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            new ContentApplyOptions(Prune: true, DryRun: true),
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
            new ContentApplyOptions(Prune: false, DryRun: false),
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
            new ContentApplyOptions(Prune: false, DryRun: false),
            CancellationToken.None
        );

        var write = Assert.Single(fake.RawWrites);
        Assert.True(write.Body is JsonObject o && o["parent"] is null);
    }

    [Fact]
    public async Task ApplyAsync_Update_SendsTheDiffsBodyAsItIs()
    {
        // #224: the diff stores the normalised body; apply must send exactly what was compared.
        var fake = new FakeUmbracoManagementClient();
        var change = Changed(Guid.NewGuid());

        await ContentApplier.ApplyAsync(
            fake,
            Diff(changed: [change]),
            new ContentApplyOptions(Prune: false, DryRun: false),
            CancellationToken.None
        );

        Assert.Equal(
            change.DesiredBody!.ToJsonString(),
            Assert.Single(fake.RawWrites).Body!.ToJsonString()
        );
    }

    [Fact]
    public async Task ApplyAsync_PruneOff_IgnoresRemovals()
    {
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        var diff = Diff(removed: [Removed(Guid.NewGuid())]);

        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            new ContentApplyOptions(Prune: false, DryRun: false),
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
            new ContentApplyOptions(Prune: true, DryRun: false),
            CancellationToken.None
        );

        Assert.Equal(new[] { c, b, a }, fake.CalledIds);
    }

    [Fact]
    public async Task ApplyAsync_Prune_SkipsTheParentOfAKeptDocument()
    {
        // Arrange: Kept is in the snapshot (placed elsewhere, which apply does not act on) but
        // still lives under Old, which the snapshot drops. Deleting Old would cascade to Kept.
        var fake = new FakeUmbracoManagementClient { DeleteContentHandler = _ => Ok() };
        Guid old = Guid.NewGuid(),
            kept = Guid.NewGuid();
        var diff = Diff(removed: [Removed(old)]) with
        {
            LiveParents = new Dictionary<Guid, Guid?> { [old] = null, [kept] = old },
        };

        // Act
        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            new ContentApplyOptions(Prune: true, DryRun: false),
            CancellationToken.None
        );

        // Assert
        Assert.Multiple(
            () => Assert.Empty(fake.CalledIds),
            () => Assert.Equal("skipped", Assert.Single(result.Data!.Actions).Status),
            () => Assert.Equal(0, result.Data!.Deleted)
        );
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
        new(TreeChangeKind.Removed, id) { DocumentTypeId = type };

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
            new ContentApplyOptions(Prune: true, DryRun: false) { Exclude = exclude },
            CancellationToken.None
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
            new ContentApplyOptions(Prune: false, DryRun: false),
            CancellationToken.None
        );

        Assert.False(result.IsSuccess);
        Assert.Single(fake.RawWrites); // stopped after the first failing write
    }

    // ── publish state (#223) ──────────────────────────────────────────────────

    private static FakeUmbracoManagementClient StateFake() =>
        new() { PublishContentHandler = _ => Ok(), UnpublishContentHandler = _ => Ok() };

    private static ContentApplyOptions Apply(bool state = true) =>
        new(Prune: false, DryRun: false) { State = state };

    [Fact]
    public async Task ApplyAsync_PublishesParentsBeforeChildrenAcrossCreatesAndUpdates()
    {
        // The parent exists on the target (an update) and the child is new (a create): the
        // create runs first, but the parent must still be published before the child.
        var fake = StateFake();
        Guid parent = Guid.NewGuid(),
            child = Guid.NewGuid();
        // The diff lists documents in snapshot pre-order, so the parent comes first even though
        // it is a change and the child an addition.
        var diff = new ContentDiff(
            [
                Changed(parent) with
                {
                    State = Publishes("en-US"),
                },
                Added(child, parent) with
                {
                    State = Publishes("en-US"),
                },
            ],
            0
        );

        await ContentApplier.ApplyAsync(fake, diff, Apply(), CancellationToken.None);

        Assert.Equal([parent, child], fake.StateCalls.Select(c => c.Id));
    }

    [Fact]
    public async Task ApplyAsync_StateOnlyChange_PublishesWithoutAnUpdate()
    {
        var fake = StateFake();
        var id = Guid.NewGuid();
        var diff = Diff(
            changed: [Changed(id) with { BodyChanged = false, State = Publishes("da-DK") }]
        );

        var result = await ContentApplier.ApplyAsync(fake, diff, Apply(), CancellationToken.None);

        Assert.Empty(fake.RawWrites);
        var call = Assert.Single(fake.StateCalls);
        Assert.Equal(("publish", id), (call.Operation, call.Id));
        Assert.Equal(["da-DK"], call.Cultures!);
        Assert.Equal(1, result.Data!.Published);
    }

    [Fact]
    public async Task ApplyAsync_InvariantDocument_PublishesTheWholeDocument()
    {
        var fake = StateFake();
        var id = Guid.NewGuid();
        var diff = Diff(
            added: [Added(id, null) with { State = new(PublishScope.WholeDocument, null) }]
        );

        var result = await ContentApplier.ApplyAsync(fake, diff, Apply(), CancellationToken.None);

        Assert.Null(Assert.Single(fake.StateCalls).Cultures);
        Assert.Null(
            result.Data!.Actions.Single(a => a.Operation == ContentOperation.Publish).Cultures
        );
    }

    [Fact]
    public async Task ApplyAsync_CultureNotPublishedInSnapshot_IsUnpublished()
    {
        var fake = StateFake();
        var id = Guid.NewGuid();
        var diff = Diff(
            changed: [Changed(id) with { BodyChanged = false, State = Unpublishes("da-DK") }]
        );

        await ContentApplier.ApplyAsync(fake, diff, Apply(), CancellationToken.None);

        var call = Assert.Single(fake.StateCalls);
        Assert.Equal(("unpublish", id), (call.Operation, call.Id));
        Assert.Equal(["da-DK"], call.Cultures!);
    }

    [Fact]
    public async Task ApplyAsync_NoState_LeavesPublishStateAlone()
    {
        var fake = StateFake();
        var diff = Diff(
            added: [Added(Guid.NewGuid(), null) with { State = Publishes("en-US") }],
            changed:
            [
                Changed(Guid.NewGuid()) with
                {
                    BodyChanged = false,
                    State = Unpublishes("da-DK"),
                },
            ]
        );

        var result = await ContentApplier.ApplyAsync(
            fake,
            diff,
            Apply(state: false),
            CancellationToken.None
        );

        Assert.Empty(fake.StateCalls);
        Assert.Equal([ContentOperation.Create], result.Data!.Actions.Select(a => a.Operation));
    }

    [Fact]
    public async Task ApplyAsync_PublishFailure_NamesThePublishStep()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ =>
                UmbracoResponse<Empty>.Failure(400, "parent not published"),
        };
        var id = Guid.NewGuid();
        var diff = Diff(added: [Added(id, null) with { State = Publishes("en-US") }]);

        var result = await ContentApplier.ApplyAsync(fake, diff, Apply(), CancellationToken.None);

        Assert.StartsWith($"Apply failed on publish document '{id}'", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyAsync_DryRunPrune_DeleteRowsNameTheDocumentAndItsType()
    {
        // #293: a prune plan read "delete 6dcb814d-..."; it has to say what that is before --yes.
        var removed = Removed(Guid.NewGuid()) with
        {
            Name = "Staging post",
            DocumentType = "blogPost",
        };

        var result = await ContentApplier.ApplyAsync(
            new FakeUmbracoManagementClient(),
            Diff(removed: [removed]),
            new ContentApplyOptions(Prune: true, DryRun: true),
            CancellationToken.None
        );

        var action = Assert.Single(result.Data!.Actions);
        Assert.Equal(("Staging post", "blogPost"), (action.Name, action.DocumentType));
    }

    [Fact]
    public async Task ApplyAsync_UnlabelledChange_RowsCarryNulls()
    {
        var result = await ContentApplier.ApplyAsync(
            new FakeUmbracoManagementClient(),
            Diff(changed: [Changed(Guid.NewGuid())]),
            new ContentApplyOptions(Prune: false, DryRun: true),
            CancellationToken.None
        );

        var action = Assert.Single(result.Data!.Actions);
        Assert.Equal((null, null), (action.Name, action.DocumentType));
    }

    private static UmbracoResponse<Empty> Ok() => UmbracoResponse<Empty>.Success(Empty.Value);
}
