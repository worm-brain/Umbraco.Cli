using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Culture and Hostnames (#180). Nothing in the CLI covered domains, so a multilingual site could
/// be built and published and still serve nothing in every culture but the default.
/// </summary>
public class DomainsWireTests
{
    private static Guid Id => Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>A document with one existing binding.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Existing() =>
        Wire.Routed(
            (
                $"document/{Id}/domains",
                """
                {
                  "defaultIsoCode": "en-US",
                  "domains": [ { "domainName": "example.com", "isoCode": "en-US" } ]
                }
                """
            )
        );

    [Fact]
    public async Task GetDomainsAsync_ReturnsTheBindings()
    {
        var result = await Wire.Client(Existing()).GetDomainsAsync(Id, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("en-US", result.Data!.DefaultIsoCode);
        var binding = Assert.Single(result.Data.Domains);
        Assert.Equal("example.com", binding.DomainName);
    }

    [Fact]
    public async Task SetDomainsAsync_SendsTheCompleteSet()
    {
        var handler = Existing();

        await Wire.Client(handler)
            .SetDomainsAsync(
                Id,
                new SetDomainsRequest
                {
                    DefaultIsoCode = "en-US",
                    Domains =
                    [
                        new DomainBinding { DomainName = "example.com", IsoCode = "en-US" },
                        new DomainBinding { DomainName = "example.com/da", IsoCode = "da-DK" },
                    ],
                },
                CancellationToken.None
            );

        // The PUT replaces, so the body carries every binding, not just the new one.
        var body = handler.BodyOf(HttpMethod.Put, $"/document/{Id}/domains");
        Assert.Equal("en-US", body["defaultIsoCode"]!.GetValue<string>());
        Assert.Equal(2, body["domains"]!.AsArray().Count);
    }

    [Fact]
    public async Task SetDomainsAsync_ReadsBackWhatTheInstanceKept()
    {
        var handler = Existing();

        var result = await Wire.Client(handler)
            .SetDomainsAsync(
                Id,
                new SetDomainsRequest
                {
                    Domains = [new DomainBinding { DomainName = "other.com", IsoCode = "fr-FR" }],
                },
                CancellationToken.None
            );

        // #181's lesson: report the instance's answer, not the request echoed back. The stub
        // still returns its original binding, and that is what the caller should see.
        Assert.Equal("example.com", Assert.Single(result.Data!.Domains).DomainName);
    }
}
