using System.Net;
using static Umbraco.Cli.Tests.RequestCountAssert;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the template delete guard costs (#432): it reads every document type to find what uses
/// the template, in batches on Umbraco 17.3+ and one at a time on 17.0-17.2, which have no batch
/// endpoint.
/// </summary>
[Collection("ConsoleCapture")]
public class TemplateDeleteRequestCountTests
{
    private static readonly Guid Template = Guid.Parse("43243243-2432-4324-3243-243243243243");

    /// <summary>A site with five document types, none of which uses the template.</summary>
    /// <param name="withBatch">False for Umbraco 17.0-17.2, whose batch path answers 404.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler Site(bool withBatch)
    {
        var types = new FakeTree();
        types.AddMany(5, null, i => FakeUmbraco.NamedItem($"Type {i}"));
        var handler = new RoutingHandler().ServeTree("document-type", types);
        if (!withBatch)
            handler.When(
                r => r.RequestUri!.AbsolutePath.EndsWith("/batch"),
                HttpStatusCode.NotFound,
                ""
            );
        return handler.ServeType("document-type", FakeUmbraco.ContentType).ElseEmpty();
    }

    [Fact]
    public async Task TemplateDelete_FiveDocumentTypes_ReadsThemInOneBatch()
    {
        // Arrange
        var cli = new HttpCli(Site(withBatch: true));

        // Act
        var run = await cli.RunAsync($"template delete {Template} --yes");

        // Assert
        run.HasRequestCount(1 + 1 + 1, "1 tree page + 1 batch for the 5 types + the delete");
    }

    [Fact]
    public async Task TemplateDelete_ServerWithoutBatchEndpoints_ReadsEachTypeById()
    {
        // Arrange
        var cli = new HttpCli(Site(withBatch: false));

        // Act
        var run = await cli.RunAsync($"template delete {Template} --yes");

        // Assert
        run.HasRequestCount(
            1 + 1 + 5 + 1,
            "1 tree page + 1 batch that 404s + 1 read per type + the delete"
        );
    }
}
