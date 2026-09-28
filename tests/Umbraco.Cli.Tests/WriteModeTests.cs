using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="WriteModes.FromReplaceFlag"/>: how a <c>--replace</c> flag becomes the named
/// <see cref="WriteMode"/> the client's updates take (#189).
/// </summary>
public class WriteModeTests
{
    /// <summary>Without <c>--replace</c> an update merges (conventions 5.1).</summary>
    [Fact]
    public void FromReplaceFlag_NotGiven_ReturnsMerge()
    {
        // Act
        var mode = WriteModes.FromReplaceFlag(false);

        // Assert
        Assert.Equal(WriteMode.Merge, mode);
    }

    /// <summary>With <c>--replace</c> an update replaces.</summary>
    [Fact]
    public void FromReplaceFlag_Given_ReturnsReplace()
    {
        // Act
        var mode = WriteModes.FromReplaceFlag(true);

        // Assert
        Assert.Equal(WriteMode.Replace, mode);
    }
}
