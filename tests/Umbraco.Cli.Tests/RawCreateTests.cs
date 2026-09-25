using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A <c>--json-body</c> create reports the id it created (#204), and <c>--id</c> decides that id
/// rather than being ignored (#218).
/// </summary>
public class RawCreateTests
{
    private static readonly Guid FlagId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid BodyId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    /// <summary>A create call that records the body it was sent.</summary>
    private sealed class Recorder
    {
        public JsonNode? Sent { get; private set; }

        public Task<UmbracoResponse<Empty>> Create(JsonNode body, CancellationToken ct)
        {
            Sent = body;
            return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
        }
    }

    private static Task<UmbracoResponse<RawCreated>> Create(string body, Guid? id, Recorder r) =>
        RawBodyCommand.CreateAsync(JsonNode.Parse(body)!, id, r.Create, CancellationToken.None);

    [Fact]
    public async Task CreateAsync_WithIdFlag_SendsAndReportsThatId()
    {
        var recorder = new Recorder();

        var result = await Create("""{"name":"Homepage Blocks"}""", FlagId, recorder);

        Assert.Equal(
            (FlagId.ToString(), new RawCreated(FlagId, "Homepage Blocks", null)),
            ((string?)recorder.Sent!["id"], result.Data)
        );
    }

    [Fact]
    public async Task CreateAsync_BodyWithItsOwnId_KeepsIt()
    {
        var result = await Create(
            $$"""{"id":"{{BodyId}}","name":"Blog Post","alias":"blogPost"}""",
            null,
            new Recorder()
        );

        Assert.Equal(new RawCreated(BodyId, "Blog Post", "blogPost"), result.Data);
    }

    [Fact]
    public async Task CreateAsync_NoIdAnywhere_SendsTheIdItReports()
    {
        var recorder = new Recorder();

        var result = await Create("""{"name":"X"}""", null, recorder);

        Assert.Equal(result.Data!.Id.ToString(), (string?)recorder.Sent!["id"]);
    }

    [Fact]
    public async Task CreateAsync_IdFlagDisagreesWithBody_RefusesWithoutSending()
    {
        var recorder = new Recorder();

        var error = await Assert.ThrowsAsync<InvalidInputException>(() =>
            Create($$"""{"id":"{{BodyId}}"}""", FlagId, recorder)
        );

        Assert.Equal(
            (true, (JsonNode?)null),
            (error.Message.Contains("does not match"), recorder.Sent)
        );
    }

    [Fact]
    public async Task CreateAsync_CreateFails_ReturnsTheFailure()
    {
        var result = await RawBodyCommand.CreateAsync(
            JsonNode.Parse("{}")!,
            null,
            (_, _) => Task.FromResult(UmbracoResponse<Empty>.Failure(400, "Alias taken")),
            CancellationToken.None
        );

        Assert.Equal((400, "Alias taken"), (result.StatusCode, result.ErrorMessage));
    }
}
