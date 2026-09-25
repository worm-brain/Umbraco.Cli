using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A write's <c>data</c> (docs/conventions.md 6.2): on success, what it acted on or the item read
/// back; a failure carries through untouched, so its category and status survive.
/// </summary>
public class WriteResultTests
{
    private static Task<UmbracoResponse<Empty>> Succeeds() =>
        Task.FromResult(UmbracoResponse<Empty>.Success(Empty.Value));

    private static Task<UmbracoResponse<Empty>> FailsWith404() =>
        Task.FromResult(UmbracoResponse<Empty>.Failure(404, "Not found."));

    [Fact]
    public async Task Then_Success_CarriesTheData()
    {
        var id = Guid.NewGuid();

        var result = await Succeeds().Then(ItemRef.Of(id));

        Assert.Equal(id.ToString(), result.Data!.Id);
    }

    [Fact]
    public async Task Then_Failure_CarriesTheFailureThrough()
    {
        var result = await FailsWith404().Then(ItemRef.Of(Guid.NewGuid()));

        Assert.Equal((false, 404), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task ThenRead_Success_ReturnsWhatTheReadReturns()
    {
        var result = await Succeeds()
            .ThenRead(() => Task.FromResult(UmbracoResponse<string>.Success("read back")));

        Assert.Equal("read back", result.Data);
    }

    [Fact]
    public async Task ThenRead_WriteFails_DoesNotRead()
    {
        var read = false;

        await FailsWith404()
            .ThenRead(() =>
            {
                read = true;
                return Task.FromResult(UmbracoResponse<string>.Success("x"));
            });

        Assert.False(read);
    }
}
