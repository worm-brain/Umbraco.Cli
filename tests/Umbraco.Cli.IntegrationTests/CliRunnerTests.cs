namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// Unit tests for <see cref="CliRunner.WithTestConfig"/>, which points the suite at a test config
/// (the dev site's) instead of the developer's own, and <see cref="CliRunner.LocateCliDll"/>, which
/// finds the CLI the suite runs. These need no live instance.
/// </summary>
public sealed class CliRunnerTests
{
    [Fact]
    public void WithTestConfig_ConfigSet_PrependsConfigOption()
    {
        // Act
        var args = CliRunner.WithTestConfig(["content", "list"], "/tmp/dev.json");

        // Assert
        Assert.Equal(["--config", "/tmp/dev.json", "content", "list"], args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithTestConfig_NoConfig_LeavesArgumentsUnchanged(string? configPath)
    {
        // Act
        var args = CliRunner.WithTestConfig(["content", "list"], configPath);

        // Assert
        Assert.Equal(["content", "list"], args);
    }

    [Theory]
    [InlineData("--config", "/tmp/own.json")]
    [InlineData("--config=/tmp/own.json", null)]
    public void WithTestConfig_CallerChoosesConfig_KeepsCallersConfig(string option, string? value)
    {
        // Arrange
        string[] given = value is null
            ? ["auth", "whoami", option]
            : ["auth", "whoami", option, value];

        // Act
        var args = CliRunner.WithTestConfig(given, "/tmp/dev.json");

        // Assert
        Assert.Equal(given, args);
    }

    [Fact]
    public void LocateCliDll_FromThisTestOutput_FindsTheBuiltCli()
    {
        // Act
        var path = CliRunner.LocateCliDll(AppContext.BaseDirectory);

        // Assert
        Assert.True(File.Exists(path), path);
    }

    [Fact]
    public void LocateCliDll_OutsideTheRepo_Throws()
    {
        // Arrange
        var outside = Directory.CreateTempSubdirectory().FullName;

        try
        {
            // Act + Assert
            Assert.Throws<FileNotFoundException>(() => CliRunner.LocateCliDll(outside));
        }
        finally
        {
            Directory.Delete(outside);
        }
    }
}
