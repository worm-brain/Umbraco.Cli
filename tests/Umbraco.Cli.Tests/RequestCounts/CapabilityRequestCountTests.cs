using System.Net;
using static Umbraco.Cli.Tests.RequestCountAssert;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What reading the site's capabilities costs (#440): a command that reports one pays for exactly
/// one manifest read, and pays it even when the read fails. Commands that report none are pinned by
/// the other request-count tests, which would fail if they started reading the manifest.
/// </summary>
[Collection("ConsoleCapture")]
public class CapabilityRequestCountTests
{
    /// <summary>A site with one dictionary item, answering the manifest read with <paramref name="manifest"/>.</summary>
    /// <param name="id">The item's id.</param>
    /// <param name="manifestStatus">The manifest endpoint's status.</param>
    /// <param name="manifest">The manifest endpoint's body.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler Site(Guid id, HttpStatusCode manifestStatus, string manifest) =>
        new RoutingHandler()
            .When(
                r =>
                    r.RequestUri!.AbsolutePath.EndsWith(
                        "/manifest/manifest",
                        StringComparison.Ordinal
                    ),
                manifestStatus,
                manifest
            )
            .When(
                r =>
                    r.RequestUri!.AbsolutePath.EndsWith(
                        $"/dictionary/{id}",
                        StringComparison.Ordinal
                    ),
                HttpStatusCode.OK,
                $$"""{"id":"{{id}}","name":"Blog.Intro","translations":[]}"""
            )
            .ElseEmpty();

    [Fact]
    public async Task DictionaryGet_ById_ReadsTheItemItsParentAndTheManifest()
    {
        // Arrange
        var id = Guid.NewGuid();
        var cli = new HttpCli(Site(id, HttpStatusCode.OK, "[]"));

        // Act
        var run = await cli.RunAsync($"dictionary get {id}");

        // Assert
        run.HasRequestCount(
            2 + 1,
            "1 by-id read + 1 ancestors read for the parent + 1 manifest read"
        );
    }

    [Fact]
    public async Task DictionaryGet_ManifestForbidden_StillSucceedsWithOneManifestRead()
    {
        // Arrange
        var id = Guid.NewGuid();
        var cli = new HttpCli(Site(id, HttpStatusCode.Forbidden, "{}"));

        // Act
        var run = await cli.RunAsync($"dictionary get {id}");

        // Assert
        run.HasRequestCount(
            2 + 1,
            "the item and its parent + 1 manifest read, which fails and is not retried"
        );
    }
}
