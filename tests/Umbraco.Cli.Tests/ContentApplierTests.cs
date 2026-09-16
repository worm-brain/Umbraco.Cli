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
