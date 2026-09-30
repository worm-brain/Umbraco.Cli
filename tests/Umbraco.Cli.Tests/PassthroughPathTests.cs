using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The path rule of <c>umbraco api</c> (ADR 0010): the site's own /umbraco/ routes only, so the
/// passthrough can never be pointed at another host or climb out of /umbraco/.
/// </summary>
public class PassthroughPathTests
{
    [Theory]
    [InlineData("/umbraco/management/api/v1/server/status")]
    [InlineData("/UMBRACO/management/api/v1/server/status")] // Umbraco's routes ignore case
    [InlineData("/umbraco/management/api/v1/tree/document/root?skip=0&take=10")]
    [InlineData("/umbraco/delivery/api/v2/content?filter=name:1.2")] // a dot in a query value
    [InlineData("/umbraco/my-package/api/v1/items")] // a package's own route
    public void Problem_PathUnderUmbraco_IsNull(string path)
    {
        Assert.Null(PassthroughPath.Problem(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("umbraco/management/api/v1/server/status")] // not from the root
    [InlineData("/somewhere/else")]
    [InlineData("https://evil.example/umbraco/management/api/v1/server/status")]
    [InlineData("//evil.example/umbraco/management/api/v1/server/status")]
    [InlineData("/umbraco/../somewhere/else")]
    [InlineData("/umbraco/./management/api/v1/server/status")]
    [InlineData("/umbraco/%2e%2e/somewhere/else")]
    [InlineData("/umbraco/%2E%2E/somewhere/else")]
    [InlineData("/umbraco/a\\..\\b")]
    [InlineData("/umbraco/management/api/v1/server/status#top")]
    [InlineData("/umbraco/management api")]
    public void Problem_PathThatCouldLeaveUmbraco_IsReported(string path)
    {
        Assert.NotNull(PassthroughPath.Problem(path));
    }
}
