using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Wire-shape tests for <c>PUT document/{id}/publish</c> (#158). Two behaviours of the 17.x
/// endpoint drive these, both established by testing against a live 17.7.0 instance:
/// <list type="bullet">
/// <item>An empty <c>schedule</c> object is not "publish now" - the server answers 200 and
/// publishes nothing. So the key must be absent unless a time was asked for.</item>
/// <item><c>"*"</c> is not a wildcard, it is the invariant culture. Sending it for a document
/// that varies by culture is rejected with 400, so "publish everything" enumerates the
/// document's own cultures instead.</item>
/// </list>
/// The previous test asserted only <c>Contains("*", body)</c>, which passed against a body that
/// published nothing - hence the assertions here are on the parsed body, not on substrings.
/// </summary>
public class ContentPublishWireTests
{
    /// <summary>Builds a client whose HTTP calls are answered by <paramref name="handler"/>.</summary>
    /// <param name="handler">The routing test double.</param>
    /// <returns>A client bound to the handler.</returns>
    private static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    /// <summary>Routes the document GET to a body with the given variant cultures.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="cultures">Variant cultures; empty means an invariant document.</param>
    /// <returns>A configured handler.</returns>
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

    /// <summary>Returns the captured publish PUT body, parsed.</summary>
    /// <param name="handler">The handler that captured the exchange.</param>
    /// <returns>The <c>publishSchedules</c> array.</returns>
    private static JsonArray Schedules(RoutingHandler handler) =>
        JsonNode.Parse(
            handler.BodyForFirst(r =>
                r.RequestUri!.AbsoluteUri.Contains("/publish", StringComparison.OrdinalIgnoreCase)
            )
        )!["publishSchedules"]!.AsArray();

    [Fact]
    public async Task PublishContentAsync_NoScheduleRequested_OmitsTheScheduleKey()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id);
        var client = Client(handler);

        var result = await client.PublishContentAsync(id, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(Schedules(handler)).AsObject();
        Assert.False(
            entry.ContainsKey("schedule"),
            "An empty schedule object makes Umbraco 17 return 200 and publish nothing (#158)."
        );
    }

    [Fact]
    public async Task PublishContentAsync_InvariantDocument_SendsANullCulture()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id);
        var client = Client(handler);

        await client.PublishContentAsync(id, ct: CancellationToken.None);

        var entry = Assert.Single(Schedules(handler)).AsObject();
        Assert.Null(entry["culture"]);
    }

    [Fact]
    public async Task PublishContentAsync_VariantDocumentWithNoCulturesNamed_EnumeratesItsCultures()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, "en-US", "da-DK");
        var client = Client(handler);

        await client.PublishContentAsync(id, ct: CancellationToken.None);

        var cultures = Schedules(handler).Select(e => e!["culture"]!.GetValue<string>()).ToList();
        Assert.Equal(["en-US", "da-DK"], cultures);
        Assert.DoesNotContain("*", cultures);
    }

    [Fact]
    public async Task PublishContentAsync_ExplicitCultures_SendsExactlyThose()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, "en-US", "da-DK");
        var client = Client(handler);

        await client.PublishContentAsync(id, ["da-DK"], ct: CancellationToken.None);

        var entry = Assert.Single(Schedules(handler)).AsObject();
        Assert.Equal("da-DK", entry["culture"]!.GetValue<string>());
    }

    [Fact]
    public async Task PublishContentAsync_ExplicitCultures_DoesNotReadTheDocument()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, "en-US");
        var client = Client(handler);

        await client.PublishContentAsync(id, ["en-US"], ct: CancellationToken.None);

        Assert.DoesNotContain(handler.Requests, u => u.AbsolutePath.EndsWith($"/document/{id}"));
    }

    [Fact]
    public async Task PublishContentAsync_ScheduleRequested_SendsTheTimesOnEachCulture()
    {
        var id = Guid.NewGuid();
        var handler = Handler(id, "en-US");
        var client = Client(handler);
        var publishAt = DateTimeOffset.Parse("2026-01-01T09:00:00Z");
        var unpublishAt = DateTimeOffset.Parse("2026-02-01T18:30:00Z");

        await client.PublishContentAsync(id, null, publishAt, unpublishAt, CancellationToken.None);

        var schedule = Assert.Single(Schedules(handler))["schedule"]!;
        Assert.Contains("2026-01-01", schedule["publishTime"]!.GetValue<string>());
        Assert.Contains("2026-02-01", schedule["unpublishTime"]!.GetValue<string>());
    }
}
