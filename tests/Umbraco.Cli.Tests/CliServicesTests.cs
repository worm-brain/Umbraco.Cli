using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The composition root <c>Program.cs</c> runs (#409): the container it builds must carry the
/// real HTTP pipeline, and the tree built from it must be the shipped one.
/// </summary>
public class CliServicesTests
{
    [Fact]
    public async Task CreateProvider_UmbracoClientUnderDryRun_RecordsTheWriteWithoutSendingIt()
    {
        // Arrange: a .invalid host cannot resolve, so a write that got past the interceptor
        // would fail the test with a transport error rather than reach anything.
        using var services = CliServices.CreateProvider();
        var state = services.GetRequiredService<MutationInterceptState>();
        state.Policy = MutationInterceptPolicy.Preview;
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("umbraco");

        // Act
        await client.PostAsync(
            "https://umbraco.invalid/umbraco/management/api/v1/webhook",
            new StringContent("""{"name":"x"}""", Encoding.UTF8, "application/json")
        );

        // Assert
        Assert.Equal("POST", Assert.Single(state.Previewed).Method);
    }

    [Fact]
    public void BuildRoot_FromCreatedProvider_HasEveryTopLevelCommandOfTheShippedTree()
    {
        using var services = CliServices.CreateProvider();

        var root = CliServices.BuildRoot(services);

        Assert.Equal(
            TestCliRoot.Build().Subcommands.Select(c => c.Name),
            root.Subcommands.Select(c => c.Name)
        );
    }

    [Fact]
    public void BuildRoot_ContainerWithoutTheCliServices_Throws()
    {
        using var empty = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => CliServices.BuildRoot(empty));
    }
}
