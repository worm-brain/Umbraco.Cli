using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Blast-radius repro for #235: the applier passes <see cref="PublishScope.WholeDocument"/> (a null
/// culture list) to unpublish an invariant document, and #235 changed what the client does with a
/// null list - it now reads the document. These drive the real applier through the real client and
/// assert the wire, so a change to either side that makes an apply send the wrong unpublish body
/// fails here rather than on a live site.
/// </summary>
public class ContentApplierUnpublishWireTests
{
    /// <summary>A handler whose document GET has the given cultures (none means invariant).</summary>
    /// <param name="id">The document id.</param>
    /// <param name="cultures">The variant cultures.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler Handler(Guid id, params string[] cultures)
    {
        var variants =
            cultures.Length == 0
                ? """[{ "culture": null, "name": "Home" }]"""
                : "["
                    + string.Join(
                        ",",
                        cultures.Select(c => $$"""{ "culture": "{{c}}", "name": "{{c}}" }""")
                    )
                    + "]";
        return new RoutingHandler()
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "")
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""{ "id": "{{id}}", "variants": {{variants}} }"""
            );
    }

    /// <summary>A state-only change that unpublishes with the given scope.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="scope">The unpublish scope.</param>
    /// <returns>The diff.</returns>
    private static ContentDiff UnpublishDiff(Guid id, PublishScope scope) =>
        new(
            [
                new ContentDocumentChange(ContentChangeKind.Changed, id)
                {
                    DesiredBody = JsonNode.Parse($$"""{"id":"{{id}}"}""")!,
                    BodyChanged = false,
                    State = new ContentPublishState.Steps(null, scope),
                },
            ],
            0
        );

    [Fact]
    public async Task ApplyAsync_InvariantDocumentUnpublishedWhole_OmitsCulturesOnTheWire()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id);

        var result = await ContentApplier.ApplyAsync(
            Wire.Client(handler),
            UnpublishDiff(id, PublishScope.WholeDocument),
            new ContentApplyOptions(Prune: false, DryRun: false) { State = true },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(
            handler.BodyOf(HttpMethod.Put, $"/document/{id}/unpublish").ContainsKey("cultures")
        );
    }

    [Fact]
    public async Task ApplyAsync_VariantCultureUnpublished_SendsExactlyThatCultureWithoutReadingTheDocument()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, "en-US", "da-DK");

        await ContentApplier.ApplyAsync(
            Wire.Client(handler),
            UnpublishDiff(id, new PublishScope(["da-DK"])),
            new ContentApplyOptions(Prune: false, DryRun: false) { State = true },
            CancellationToken.None
        );

        var cultures = handler.BodyOf(HttpMethod.Put, $"/document/{id}/unpublish")["cultures"]!
            .AsArray()
            .Select(c => c!.GetValue<string>())
            .ToList();
        Assert.Equal(["da-DK"], cultures);
        handler.AssertNoRequest(HttpMethod.Get, $"/document/{id}");
    }
}
