using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A response the client cannot read as expected (#154) fails as <c>unexpected_response</c>, the
/// likely sign of an Umbraco version the generated client was not built against, rather than as
/// a generic error or a success with defaulted fields.
/// </summary>
public class UnexpectedResponseWireTests
{
    private static readonly Guid Id = Guid.Parse("3f7a8b2e-0000-4000-8000-000000000001");

    [Fact]
    public async Task GetWebhookEventsAsync_GarbageBody_IsAnUnexpectedResponse()
    {
        var result = await Wire.Client(Wire.Returning("garbage"))
            .GetWebhookEventsAsync(0, 10, CancellationToken.None);

        Assert.Equal(FailureCategory.UnexpectedResponse, result.Category);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_HtmlBody_NamesVersionDriftAsTheLikelyCause()
    {
        // A proxy's login page is the everyday case of a body that is not JSON.
        var result = await Wire.Client(Wire.Returning("<html>login</html>"))
            .GetWebhookEventsAsync(0, 10, CancellationToken.None);

        Assert.Contains("Umbraco version the CLI was not built for", result.ErrorMessage);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_GarbageBody_ReportsNoHttpStatus()
    {
        // The response was a 200; there is no error status to report.
        var result = await Wire.Client(Wire.Returning("garbage"))
            .GetWebhookEventsAsync(0, 10, CancellationToken.None);

        Assert.Equal(0, result.StatusCode);
    }

    [Fact]
    public async Task GetContentByIdAsync_NoVariants_IsAnUnexpectedResponseNotADefaultedSuccess()
    {
        // Without a variant the name would be "" and the create date 0001-01-01.
        var handler = Wire.Routed(($"/document/{Id}", $$"""{ "id": "{{Id}}", "variants": [] }"""));

        var result = await Wire.Client(handler).GetContentByIdAsync(Id, CancellationToken.None);

        Assert.Equal(FailureCategory.UnexpectedResponse, result.Category);
    }

    [Fact]
    public async Task GetContentByIdAsync_VariantWithoutAName_IsAnUnexpectedResponse()
    {
        // A renamed field is exactly what drift looks like: well-formed JSON, the key missing.
        var handler = Wire.Routed(
            ($"/document/{Id}", $$"""{ "id": "{{Id}}", "variants": [ { "title": "Post" } ] }""")
        );

        var result = await Wire.Client(handler).GetContentByIdAsync(Id, CancellationToken.None);

        Assert.Equal(FailureCategory.UnexpectedResponse, result.Category);
    }

    [Fact]
    public async Task GetContentByIdAsync_NamedVariant_Succeeds()
    {
        var handler = Wire.Routed(
            ($"/document/{Id}", $$"""{ "id": "{{Id}}", "variants": [ { "name": "Post" } ] }""")
        );

        var result = await Wire.Client(handler).GetContentByIdAsync(Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task GetMediaByIdAsync_NoVariants_IsAnUnexpectedResponse()
    {
        var handler = Wire.Routed(($"/media/{Id}", $$"""{ "id": "{{Id}}", "variants": [] }"""));

        var result = await Wire.Client(handler).GetMediaByIdAsync(Id, CancellationToken.None);

        Assert.Equal(FailureCategory.UnexpectedResponse, result.Category);
    }

    [Fact]
    public async Task GetMediaByIdAsync_NoVariants_NamesTheEndpoint()
    {
        var handler = Wire.Routed(($"/media/{Id}", $$"""{ "id": "{{Id}}", "variants": [] }"""));

        var result = await Wire.Client(handler).GetMediaByIdAsync(Id, CancellationToken.None);

        Assert.StartsWith($"GET media/{Id} returned no variant with a name.", result.ErrorMessage);
    }
}
