using System.Net;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Umbraco's generic placement refusals say what to check (#363, #395). A move, copy or restore
/// of a document or media type that is not allowed where it is going gets a 400 <c>NotAllowed</c> blaming "a
/// permission/configuration mismatch", and a sort naming an id that is not a child gets a 400
/// <c>SortingInvalid</c> that names nothing.
/// </summary>
public class PlacementRefusalWireTests
{
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Parent = Guid.NewGuid();

    private const string NotAllowed = """
        {"title":"Operation not permitted","detail":"The attempted operation was not permitted, likely due to a permission/configuration mismatch with the operation.","status":400,"operationStatus":"NotAllowed"}
        """;

    /// <summary>Moves <see cref="Item"/> against a server that answers every request with a 400.</summary>
    /// <param name="parent">The target parent, or null for the root.</param>
    /// <param name="body">The 400 body.</param>
    /// <returns>The client's response.</returns>
    private static Task<UmbracoResponse<Empty>> MoveRefused(Guid? parent, string body) =>
        Wire.Client(new RoutingHandler().When(_ => true, HttpStatusCode.BadRequest, body))
            .MoveContentAsync(Item, parent, CancellationToken.None);

    [Fact]
    public async Task MoveContentAsync_NotAllowedUnderAParent_NamesTheParentAndWhatToCheck()
    {
        var result = await MoveRefused(Parent, NotAllowed);

        Assert.StartsWith(
            $"Umbraco would not move {Item} under {Parent}: Operation not permitted",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task MoveContentAsync_NotAllowedAtTheRoot_SaysTheRoot()
    {
        var result = await MoveRefused(null, NotAllowed);

        Assert.Contains("at the content root", result.ErrorMessage);
    }

    [Fact]
    public async Task MoveContentAsync_NotAllowed_KeepsUmbracosBodyAsTheDetails()
    {
        var result = await MoveRefused(Parent, NotAllowed);

        Assert.Equal("NotAllowed", result.Details?["operationStatus"]?.ToString());
    }

    [Fact]
    public async Task MoveContentAsync_OtherRejection_IsLeftAsUmbracoSaidIt()
    {
        var result = await MoveRefused(
            Parent,
            """{"title":"Parent not found","status":400,"operationStatus":"ParentNotFound"}"""
        );

        Assert.StartsWith("Parent not found (ParentNotFound)", result.ErrorMessage);
    }

    [Fact]
    public void ExplainPlacementRefusal_CopyRefusedWithoutAStatusCode_IsReworded()
    {
        // The copy path reads the refusal without its title or operationStatus.
        var refused = UmbracoResponse<ContentItemResponse>.Failure(
            400,
            "The attempted operation was not permitted, likely due to a permission/configuration mismatch with the operation."
        );

        var result = UmbracoManagementClient.ExplainPlacementRefusal(refused, "copy", Item, null);

        Assert.StartsWith(
            $"Umbraco would not copy {Item} at the content root",
            result.ErrorMessage
        );
    }

    // ── media move (#395) ─────────────────────────────────────────────────────────

    /// <summary>Moves media <see cref="Item"/> against a server that answers every request with a 400.</summary>
    /// <param name="parent">The target folder, or null for the media root.</param>
    /// <param name="body">The 400 body.</param>
    /// <returns>The client's response.</returns>
    private static Task<UmbracoResponse<Empty>> MoveMediaRefused(Guid? parent, string body) =>
        Wire.Client(new RoutingHandler().When(_ => true, HttpStatusCode.BadRequest, body))
            .MoveMediaAsync(Item, parent, CancellationToken.None);

    [Fact]
    public async Task MoveMediaAsync_NotAllowedUnderAParent_NamesTheParentAndTheMediaTypeCheck()
    {
        var result = await MoveMediaRefused(Parent, NotAllowed);

        Assert.Matches(
            $"^Umbraco would not move {Item} under {Parent}: Operation not permitted.*"
                + "check the parent's allowed media types .* with 'media-type get <id>'",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task MoveMediaAsync_NotAllowedAtTheRoot_SaysTheMediaRoot()
    {
        var result = await MoveMediaRefused(null, NotAllowed);

        Assert.Contains("at the media root", result.ErrorMessage);
    }

    [Fact]
    public async Task MoveMediaAsync_NotAllowed_KeepsUmbracosBodyAsTheDetails()
    {
        var result = await MoveMediaRefused(Parent, NotAllowed);

        Assert.Equal("NotAllowed", result.Details?["operationStatus"]?.ToString());
    }

    [Fact]
    public async Task MoveMediaAsync_OtherRejection_IsLeftAsUmbracoSaidIt()
    {
        var result = await MoveMediaRefused(
            Parent,
            """{"title":"Parent not found","status":400,"operationStatus":"ParentNotFound"}"""
        );

        Assert.StartsWith("Parent not found (ParentNotFound)", result.ErrorMessage);
    }

    // ── content restore (#395) ───────────────────────────────────────────────────

    /// <summary>Restores <see cref="Item"/> under <see cref="Parent"/> against a server that answers with a 400.</summary>
    /// <param name="body">The 400 body.</param>
    /// <returns>The client's response.</returns>
    private static Task<UmbracoResponse<Empty>> RestoreRefused(string body) =>
        Wire.Client(new RoutingHandler().When(_ => true, HttpStatusCode.BadRequest, body))
            .RestoreContentAsync(Item, RestoreTarget.Under(Parent), CancellationToken.None);

    [Fact]
    public async Task RestoreContentAsync_NotAllowed_KeepsUmbracosBodyAsTheDetails()
    {
        var result = await RestoreRefused(NotAllowed);

        Assert.Equal("NotAllowed", result.Details?["operationStatus"]?.ToString());
    }

    [Fact]
    public async Task RestoreContentAsync_NotAllowed_NamesTheTarget()
    {
        var result = await RestoreRefused(NotAllowed);

        Assert.StartsWith(
            $"Umbraco would not restore {Item} under {Parent}: Operation not permitted",
            result.ErrorMessage
        );
    }

    [Fact]
    public async Task RestoreContentAsync_OtherRejection_IsLeftAsUmbracoSaidIt()
    {
        var result = await RestoreRefused(
            """{"title":"Parent not found","status":400,"operationStatus":"ParentNotFound"}"""
        );

        Assert.StartsWith("Parent not found (ParentNotFound)", result.ErrorMessage);
    }

    // ── sort ────────────────────────────────────────────────────────────────────

    /// <summary>One page holding the given children.</summary>
    /// <param name="children">The child ids.</param>
    /// <returns>A page reader over them.</returns>
    private static Func<int, int, Task<UmbracoResponse<PagedResponse<Guid>>>> Children(
        params Guid[] children
    ) =>
        (_, _) =>
            Task.FromResult(
                UmbracoResponse<PagedResponse<Guid>>.Success(
                    new PagedResponse<Guid> { Total = children.Length, Items = children }
                )
            );

    private static readonly UmbracoResponse<Empty> SortingInvalid = UmbracoResponse<Empty>.Failure(
        400,
        "Invalid sorting options (SortingInvalid): The supplied sorting operations were invalid."
    );

    [Fact]
    public async Task ExplainRefusalAsync_OrderNamesAStranger_NamesIt()
    {
        var child = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        var result = await ChildSort.ExplainRefusalAsync(
            SortingInvalid,
            [child, stranger],
            Children(child),
            id => id,
            "the content root",
            "umbraco content list"
        );

        Assert.StartsWith($"Not children of the content root: {stranger}.", result.ErrorMessage);
    }

    [Fact]
    public async Task ExplainRefusalAsync_EveryIdIsAChild_LeavesTheMessage()
    {
        var child = Guid.NewGuid();

        var result = await ChildSort.ExplainRefusalAsync(
            SortingInvalid,
            [child],
            Children(child),
            id => id,
            "the content root",
            "umbraco content list"
        );

        Assert.Equal(SortingInvalid.ErrorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task ExplainRefusalAsync_Success_DoesNotReadTheChildren()
    {
        var read = false;

        await ChildSort.ExplainRefusalAsync(
            UmbracoResponse<Empty>.Success(Empty.Value),
            [Guid.NewGuid()],
            (_, _) =>
            {
                read = true;
                return Children()(0, 0);
            },
            id => id,
            "the content root",
            "umbraco content list"
        );

        Assert.False(read);
    }
}
