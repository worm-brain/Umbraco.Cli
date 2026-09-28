using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>The tested Umbraco version range (#153).</summary>
public class VersionSupportTests
{
    [Theory]
    [InlineData("17.3.5")]
    [InlineData("17.0.0-rc2")]
    [InlineData("18.1.0+build.7")]
    public void Check_VersionInsideTheRange_IsSupported(string version)
    {
        Assert.Equal(VersionFit.Supported, VersionSupport.Check(version));
    }

    [Theory]
    [InlineData("14.3.0")]
    [InlineData("19.0.0")]
    public void Check_VersionOutsideTheRange_IsUnsupported(string version)
    {
        Assert.Equal(VersionFit.Unsupported, VersionSupport.Check(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Latest")]
    public void Check_UnreadableVersion_IsUnknown(string? version)
    {
        Assert.Equal(VersionFit.Unknown, VersionSupport.Check(version));
    }

    [Fact]
    public void OutOfRangeMessage_NamesBothVersions()
    {
        var message = VersionSupport.OutOfRangeMessage("19.0.0");

        Assert.Contains(
            $"Umbraco 19.0.0, outside the range this CLI was built and tested against (Umbraco {VersionSupport.Range})",
            message
        );
    }

    [Fact]
    public void Range_TwoMajors_ReadsAsARange()
    {
        Assert.Equal(
            $"{VersionSupport.MinMajor}.x-{VersionSupport.MaxMajor}.x",
            VersionSupport.Range
        );
    }
}
