using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The type usage the document and media type delete guards count (#287), against the real client:
/// items are counted from the tree (whose items carry their type) plus the recycle bin, the walk is
/// shared by every type checked, and composition users and element types are reported.
/// </summary>
public class TypeUsageWireTests
{
    private static readonly Guid Page = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("33333333-0000-0000-0000-000000000002");
    private static readonly Guid Home = Guid.Parse("44444444-0000-0000-0000-000000000001");

    /// <summary>
    /// Two Page documents in the tree (one nested), one Other document, one trashed Page
    /// document, and Blog Post using Page as a composition.
    /// </summary>
    private static RoutingHandler Documents() =>
        Wire.Routed(
            (
                "tree/document/root",
                $$$"""{"total":2,"items":[{"id":"{{{Home}}}","hasChildren":true,"documentType":{"id":"{{{Page}}}"}},{"id":"{{{Guid.NewGuid()}}}","hasChildren":false,"documentType":{"id":"{{{Other}}}"}}]}"""
            ),
            (
                "tree/document/children",
                $$$"""{"total":1,"items":[{"id":"{{{Guid.NewGuid()}}}","hasChildren":false,"documentType":{"id":"{{{Page}}}"}}]}"""
            ),
            (
                "recycle-bin/document/root",
                $$$"""{"total":1,"items":[{"id":"{{{Guid.NewGuid()}}}","hasChildren":false,"documentType":{"id":"{{{Page}}}"}}]}"""
            ),
            ("composition-references", $$$"""[{"id":"{{{Other}}}","name":"Blog Post"}]"""),
            ("document-type/", """{"isElement":false}""")
        );

    [Fact]
    public async Task GetDocumentTypeUsageAsync_CountsTheTreeAndTheRecycleBin()
    {
        var result = await Wire.Client(Documents()).GetDocumentTypeUsageAsync(Page);

        Assert.Equal(3, result.Data!.Items);
    }

    [Fact]
    public async Task GetDocumentTypeUsageAsync_NamesTheTypesThatComposeIt()
    {
        var result = await Wire.Client(Documents()).GetDocumentTypeUsageAsync(Page);

        Assert.Equal(["Blog Post"], result.Data!.ComposedBy);
    }

    [Fact]
    public async Task GetDocumentTypeUsageAsync_TwoTypes_WalksTheTreeOnce()
    {
        // A prune of several types, or a delete of several ids, must not walk the tree per type.
        var handler = Documents();
        var client = Wire.Client(handler);

        await client.GetDocumentTypeUsageAsync(Page);
        await client.GetDocumentTypeUsageAsync(Other);

        Assert.Single(handler.Requests, u => u.AbsoluteUri.Contains("tree/document/root"));
    }

    [Fact]
    public async Task GetDocumentTypeUsageAsync_ElementType_IsReportedAsOne()
    {
        var handler = Wire.Routed(
            ("tree/document/root", """{"total":0,"items":[]}"""),
            ("recycle-bin/document/root", """{"total":0,"items":[]}"""),
            ("composition-references", "[]"),
            ("document-type/", """{"isElement":true}""")
        );

        var result = await Wire.Client(handler).GetDocumentTypeUsageAsync(Page);

        Assert.True(result.Data!.IsElement);
    }

    [Fact]
    public async Task GetDocumentTypeUsageAsync_TreeUnreadable_Fails()
    {
        // An unknown is not a yes: the guard refuses when the count could not be read.
        var handler = new RoutingHandler()
            .When(
                r => r.RequestUri!.AbsoluteUri.Contains("tree/document/root"),
                HttpStatusCode.InternalServerError,
                "{}"
            )
            .When(_ => true, HttpStatusCode.OK, "[]");

        var result = await Wire.Client(handler).GetDocumentTypeUsageAsync(Page);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetMediaTypeUsageAsync_CountsTheTreeAndTheRecycleBin()
    {
        var handler = Wire.Routed(
            (
                "tree/media/root",
                $$$"""{"total":1,"items":[{"id":"{{{Guid.NewGuid()}}}","hasChildren":false,"mediaType":{"id":"{{{Page}}}"}}]}"""
            ),
            (
                "recycle-bin/media/root",
                $$$"""{"total":1,"items":[{"id":"{{{Guid.NewGuid()}}}","hasChildren":false,"mediaType":{"id":"{{{Page}}}"}}]}"""
            ),
            ("composition-references", "[]")
        );

        var result = await Wire.Client(handler).GetMediaTypeUsageAsync(Page);

        Assert.Equal(2, result.Data!.Items);
    }
}
