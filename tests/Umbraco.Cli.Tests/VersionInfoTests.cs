using System.Reflection;
using Umbraco.Cli.Infrastructure;
using Xunit;

namespace Umbraco.Cli.Tests;

/// <summary>Tests for <see cref="VersionInfo"/>, the source of <c>umbraco --version</c> output (#95).</summary>
public class VersionInfoTests
{
    [Fact]
    public void NormalizeVersion_WithSourceLinkSuffix_StripsBuildMetadata()
    {
        var result = VersionInfo.NormalizeVersion("0.1.0+3fa6653abc", assemblyVersion: null);

        Assert.Equal("0.1.0", result);
    }

    [Fact]
    public void NormalizeVersion_NoInformationalVersion_FallsBackToAssemblyVersion()
    {
        var result = VersionInfo.NormalizeVersion(null, new System.Version(2, 3, 4, 0));

        Assert.Equal("2.3.4.0", result);
    }

    [Fact]
    public void NormalizeVersion_NothingAvailable_ReturnsUnknown()
    {
        var result = VersionInfo.NormalizeVersion(null, assemblyVersion: null);

        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ToMoniker_NetCoreAppFrameworkName_ReturnsShortMoniker()
    {
        var result = VersionInfo.ToMoniker(".NETCoreApp,Version=v9.0");

        Assert.Equal("net9.0", result);
    }

    [Fact]
    public void ToMoniker_Null_ReturnsUnknown()
    {
        var result = VersionInfo.ToMoniker(null);

        Assert.Equal("unknown", result);
    }

    [Fact]
    public void Format_IncludesToolNameVersionAndFramework()
    {
        var text = VersionInfo.Format(Assembly.GetExecutingAssembly());

        Assert.Contains("umbraco", text);
        Assert.Contains("target framework:", text);
        Assert.Contains("runtime:", text);
    }
}
