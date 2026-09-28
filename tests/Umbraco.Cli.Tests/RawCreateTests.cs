using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A <c>--json-body</c> create settles its id before sending (#204), lets <c>--id</c> decide it
/// (#218), and returns the item read back by that id rather than the body it sent (#285).
/// </summary>
public class RawCreateTests
{
    private static readonly Guid FlagId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid BodyId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    /// <summary>
    /// A create call that records the body it was sent, and a read that returns what the
    /// "instance" saved: the sent body plus a property the body never had, so a test can tell a
    /// read-back from an echo.
    /// </summary>
    private sealed class Recorder
    {
        public JsonNode? Sent { get; private set; }

        public Guid? ReadId { get; private set; }

        public bool ReadFails { get; init; }

        public Task<UmbracoResponse<Empty>> Create(JsonNode body, CancellationToken ct)
        {
            Sent = body.DeepClone();
            return Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));
        }

        public Task<UmbracoResponse<JsonNode>> Read(Guid id, CancellationToken ct)
        {
            ReadId = id;
            if (ReadFails)
                return Task.FromResult(UmbracoResponse<JsonNode>.Failure(503, "Unavailable"));
            var saved = Sent!.DeepClone();
            saved["properties"] = new JsonArray(new JsonObject { ["alias"] = "title" });
            return Task.FromResult(UmbracoResponse<JsonNode>.Success(saved));
        }
    }

    private static Task<UmbracoResponse<JsonNode>> Create(string body, Guid? id, Recorder r) =>
        RawBodyCommand.CreateAsync(
            JsonNode.Parse(body)!,
            id,
            r.Create,
            r.Read,
            CancellationToken.None
        );

    [Fact]
    public async Task CreateAsync_WithIdFlag_SendsAndReadsBackThatId()
    {
        var recorder = new Recorder();

        await Create("""{"name":"Homepage Blocks"}""", FlagId, recorder);

        Assert.Equal(
            (FlagId.ToString(), (Guid?)FlagId),
            ((string?)recorder.Sent!["id"], recorder.ReadId)
        );
    }

    [Fact]
    public async Task CreateAsync_BodyWithItsOwnId_KeepsIt()
    {
        var recorder = new Recorder();

        await Create($$"""{"id":"{{BodyId}}","name":"Blog Post"}""", null, recorder);

        Assert.Equal(BodyId, recorder.ReadId);
    }

    [Fact]
    public async Task CreateAsync_NoIdAnywhere_ReadsBackTheIdItSent()
    {
        var recorder = new Recorder();

        await Create("""{"name":"X"}""", null, recorder);

        Assert.Equal((string?)recorder.Sent!["id"], recorder.ReadId.ToString());
    }

    [Fact]
    public async Task CreateAsync_Success_ReturnsTheSavedItemNotTheBody()
    {
        var result = await Create("""{"name":"Blog Post","properties":[]}""", null, new Recorder());

        Assert.Equal("title", (string?)result.Data!["properties"]![0]!["alias"]);
    }

    [Fact]
    public async Task CreateAsync_ReadBackFails_StillSucceedsWithTheId()
    {
        var result = await Create("""{"name":"X"}""", FlagId, new Recorder { ReadFails = true });

        Assert.Equal(
            (true, """{"id":"11111111-2222-3333-4444-555555555555"}"""),
            (result.IsSuccess, result.Data!.ToJsonString())
        );
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
    public async Task CreateAsync_CreateFails_ReturnsTheFailureWithoutReading()
    {
        var recorder = new Recorder();

        var result = await RawBodyCommand.CreateAsync(
            JsonNode.Parse("{}")!,
            null,
            (_, _) => Task.FromResult(UmbracoResponse<Empty>.Failure(400, "Alias taken")),
            recorder.Read,
            CancellationToken.None
        );

        Assert.Equal(
            (400, "Alias taken", (Guid?)null),
            (result.StatusCode, result.ErrorMessage, recorder.ReadId)
        );
    }
}
