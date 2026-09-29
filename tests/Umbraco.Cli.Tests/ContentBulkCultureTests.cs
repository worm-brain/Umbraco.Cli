using System.Net;
using System.Text.Json.Nodes;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The cultures <c>content bulk publish</c> and <c>unpublish</c> send when none is named, now that
/// they come from one <c>item/document</c> read for the batch (#414): each document still gets
/// exactly its own cultures, an invariant one gets none, and <c>"*"</c> is never sent (#158, #235).
/// </summary>
[Collection("ConsoleCapture")]
public sealed class ContentBulkCultureTests : IDisposable
{
    private static readonly Guid Variant = Guid.NewGuid();
    private static readonly Guid Invariant = Guid.NewGuid();

    private readonly string _dir = Directory.CreateTempSubdirectory("umbraco-bulk-").FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task BulkPublish_NoCulture_PublishesEachDocumentInItsOwnCultures()
    {
        // Arrange
        var handler = MixedSite();

        // Act
        await new HttpCli(handler).RunAsync(
            $"content bulk publish --file {IdFile(Variant, Invariant)}"
        );

        // Assert
        Assert.Equal<string?>(["da-DK", "en-US"], PublishedCultures(handler, Variant, "publish"));
        Assert.Equal<string?>([null], PublishedCultures(handler, Invariant, "publish"));
    }

    [Fact]
    public async Task BulkUnpublish_NoCulture_ListsAVariantDocumentsCulturesAndOmitsThemForAnInvariantOne()
    {
        // Arrange
        var handler = MixedSite();

        // Act
        await new HttpCli(handler).RunAsync(
            $"content bulk unpublish --file {IdFile(Variant, Invariant)} --yes"
        );

        // Assert
        Assert.Equal(
            """["da-DK","en-US"]""",
            Body(handler, Variant, "unpublish")["cultures"]?.ToJsonString()
        );
        Assert.Null(Body(handler, Invariant, "unpublish")["cultures"]);
    }

    [Fact]
    public async Task BulkPublish_IdTheBatchReadLeavesOut_ReportsThatItemAndPublishesTheRest()
    {
        // Arrange: the batch does not return the missing id, and its own read is a 404.
        var missing = Guid.NewGuid();
        var handler = new RoutingHandler()
            .ServeToken()
            .ServeDocumentItems(id =>
                id == Variant ? FakeUmbraco.DocumentItemEntry(id, "en-US") : null
            )
            .When(
                r =>
                    r.Method == HttpMethod.Get
                    && r.RequestUri!.AbsolutePath.EndsWith($"/document/{missing}"),
                HttpStatusCode.NotFound,
                """{"title":"The document could not be found"}"""
            )
            .ElseEmpty();

        // Act
        var run = await new HttpCli(handler).RunAsync(
            $"content bulk publish --file {IdFile(missing, Variant)}"
        );

        // Assert
        var items = JsonNode.Parse(run.Stdout)!["data"]!.AsArray();
        Assert.Equal("error", (string?)items[0]!["status"]);
        Assert.Equal("success", (string?)items[1]!["status"]);
        Assert.Equal<string?>(["en-US"], PublishedCultures(handler, Variant, "publish"));
    }

    [Fact]
    public async Task BulkPublish_BatchReadFails_ReadsEachDocumentInsteadAndPublishesIt()
    {
        // Arrange: item/document answers 500; the by-id reads still work.
        var handler = new RoutingHandler()
            .ServeToken()
            .When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/item/document"),
                HttpStatusCode.InternalServerError,
                """{"title":"Boom"}"""
            )
            .ServeById("document", FakeUmbraco.Document)
            .ElseEmpty();

        // Act
        var run = await new HttpCli(handler).RunAsync(
            $"content bulk publish --file {IdFile(Variant, Invariant)}"
        );

        // Assert
        Assert.Equal(0, run.Exit);
    }

    /// <summary>
    /// A site with a document in two cultures (<see cref="Variant"/>) and an invariant one
    /// (<see cref="Invariant"/>), served only through <c>item/document</c>: a by-id document read
    /// would get an empty 200 and fail, so a passing test proves the batch was used.
    /// </summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler MixedSite() =>
        new RoutingHandler()
            .ServeToken()
            .ServeDocumentItems(id =>
                id == Variant ? FakeUmbraco.DocumentItemEntry(id, "da-DK", "en-US")
                : id == Invariant ? FakeUmbraco.DocumentItemEntry(id)
                : null
            )
            .ElseEmpty();

    /// <summary>Writes the ids one per line to a file for <c>--file</c>.</summary>
    /// <param name="ids">The ids, in order.</param>
    /// <returns>The file's path.</returns>
    private string IdFile(params Guid[] ids)
    {
        var file = Path.Combine(_dir, "ids.txt");
        File.WriteAllLines(file, ids.Select(id => id.ToString()));
        return file;
    }

    /// <summary>The body of the <c>PUT document/{id}/{verb}</c> the run sent.</summary>
    /// <param name="handler">The handler that recorded the run.</param>
    /// <param name="id">The document.</param>
    /// <param name="verb"><c>publish</c> or <c>unpublish</c>.</param>
    /// <returns>The parsed body.</returns>
    private static JsonObject Body(RoutingHandler handler, Guid id, string verb) =>
        JsonNode
            .Parse(
                handler.BodyForFirst(r =>
                    r.Method == HttpMethod.Put
                    && r.Uri.AbsolutePath.EndsWith($"/document/{id}/{verb}")
                )
            )!
            .AsObject();

    /// <summary>The culture of each publish schedule the publish of <paramref name="id"/> sent.</summary>
    /// <param name="handler">The handler that recorded the run.</param>
    /// <param name="id">The document.</param>
    /// <param name="verb"><c>publish</c>.</param>
    /// <returns>The cultures, null for the invariant culture.</returns>
    private static IEnumerable<string?> PublishedCultures(
        RoutingHandler handler,
        Guid id,
        string verb
    ) =>
        [
            .. Body(handler, id, verb)["publishSchedules"]!
                .AsArray()
                .Select(s => s!["culture"]?.GetValue<string>()),
        ];
}
