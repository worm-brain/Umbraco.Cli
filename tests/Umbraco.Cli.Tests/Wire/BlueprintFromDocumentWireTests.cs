using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>document-blueprint create --from-document</c> finishing what Umbraco 17.7 leaves undone (#240): the
/// blueprint lands at the root whatever <c>parent</c> says, only the default-language variant takes
/// the new name, and the response has no top-level name.
/// </summary>
public class BlueprintFromDocumentWireTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid Folder = Guid.NewGuid();

    /// <summary>A handler whose blueprint GET has an en-US and a da-DK variant with these names.</summary>
    /// <param name="enName">The en-US variant name.</param>
    /// <param name="daName">The da-DK variant name.</param>
    /// <param name="moveStatus">The status the move PUT answers with.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler Handler(
        string enName = "Starter",
        string daName = "En begynderguide",
        HttpStatusCode moveStatus = HttpStatusCode.OK
    ) =>
        new RoutingHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/move"), moveStatus, "")
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""
                { "id": "{{Id}}", "values": [], "variants": [
                  { "culture": "en-US", "segment": null, "name": "{{enName}}" },
                  { "culture": "da-DK", "segment": null, "name": "{{daName}}" }
                ] }
                """
            )
            .When(_ => true, HttpStatusCode.OK, "");

    /// <summary>A from-document request for the fixed id, optionally under the folder.</summary>
    /// <param name="withParent">Whether to ask for the folder as the parent.</param>
    /// <returns>The request.</returns>
    private static CreateBlueprintFromDocumentRequest Request(bool withParent = true) =>
        new()
        {
            Id = Id,
            Document = Guid.NewGuid(),
            Name = "Starter",
            Parent = withParent ? new ContentParentReference { Id = Folder } : null,
        };

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_WithParent_MovesTheBlueprintThere()
    {
        var handler = Handler();

        var result = await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var move = handler.BodyOf(HttpMethod.Put, $"/document-blueprint/{Id}/move");
        Assert.Equal(Folder.ToString(), move["target"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_WithoutParent_DoesNotMove()
    {
        var handler = Handler();

        await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(
                Request(withParent: false),
                CancellationToken.None
            );

        handler.AssertNoRequest(HttpMethod.Put, $"/document-blueprint/{Id}/move");
    }

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_OtherCultureKeepsSourceName_RenamesEveryVariant()
    {
        var handler = Handler(daName: "En begynderguide");

        await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(Request(), CancellationToken.None);

        var names = handler.BodyOf(HttpMethod.Put, $"/document-blueprint/{Id}")["variants"]!
            .AsArray()
            .Select(v => v!["name"]!.GetValue<string>())
            .ToList();
        Assert.Equal(["Starter", "Starter"], names);
    }

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_EveryVariantAlreadyNamed_SendsNoRename()
    {
        var handler = Handler(daName: "Starter");

        await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(Request(), CancellationToken.None);

        handler.AssertNoRequest(HttpMethod.Put, $"/document-blueprint/{Id}");
    }

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_ReturnsTheNameAtTheTopLevel()
    {
        var handler = Handler();

        var result = await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(Request(), CancellationToken.None);

        Assert.Equal("Starter", result.Data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateDocumentBlueprintFromDocumentAsync_MoveRejected_FailsNamingTheCreatedBlueprint()
    {
        var handler = Handler(moveStatus: HttpStatusCode.BadRequest);

        var result = await Wire.Client(handler)
            .CreateDocumentBlueprintFromDocumentAsync(Request(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        Assert.StartsWith(
            $"Blueprint {Id} was created, but was left at the root",
            result.ErrorMessage
        );
    }
}
