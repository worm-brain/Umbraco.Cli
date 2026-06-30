using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-cli-test-{Guid.NewGuid()}.json"
    );

    private ConfigStore Store => new(_tempPath);

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);

        // Safety net: clear env vars so a failed test doesn't poison later tests.
        Environment.SetEnvironmentVariable("UMBRACO_HOST", null);
        Environment.SetEnvironmentVariable("UMBRACO_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("UMBRACO_CLIENT_SECRET", null);
    }

    // ── Save / Load roundtrip ─────────────────────────────────────────────────

    [Fact]
    public void SaveThenLoad_PreservesAllFields()
    {
        var original = new CliConfig
        {
            Host = "https://example.umbraco.io",
            ClientId = "my-client-id",
            ClientSecret = "s3cr3t!",
        };

        Store.Save(original);
        var loaded = Store.Load();

        Assert.Equal("https://example.umbraco.io", loaded.Host);
        Assert.Equal("my-client-id", loaded.ClientId);
        Assert.Equal("s3cr3t!", loaded.ClientSecret);
    }

    [Fact]
    public void Load_WhenFileAbsent_ReturnsEmptyConfig()
    {
        var loaded = Store.Load();
        Assert.Null(loaded.Host);
        Assert.Null(loaded.ClientId);
        Assert.Null(loaded.ClientSecret);
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        var deepPath = Path.Combine(
            Path.GetTempPath(),
            $"umbraco-deep-{Guid.NewGuid()}",
            "sub",
            "config.json"
        );
        try
        {
            new ConfigStore(deepPath).Save(new CliConfig { Host = "https://test.com" });
            Assert.True(File.Exists(deepPath));
        }
        finally
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(deepPath))!;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesFile()
    {
        Store.Save(new CliConfig { Host = "https://example.com" });
        Assert.True(File.Exists(_tempPath));

        Store.Delete();

        Assert.False(File.Exists(_tempPath));
    }

    [Fact]
    public void Delete_WhenFileAbsent_DoesNotThrow()
    {
        var ex = Record.Exception(() => Store.Delete());
        Assert.Null(ex);
    }

    // ── IsComplete ────────────────────────────────────────────────────────────

    [Fact]
    public void IsComplete_AllFieldsSet_ReturnsTrue()
    {
        var cfg = new CliConfig
        {
            Host = "https://host.com",
            ClientId = "id",
            ClientSecret = "s",
        };
        Assert.True(cfg.IsComplete);
    }

    [Theory]
    [InlineData(null, "id", "secret")]
    [InlineData("https://host.com", null, "secret")]
    [InlineData("https://host.com", "id", null)]
    [InlineData("", "id", "secret")]
    [InlineData("https://host.com", "", "secret")]
    public void IsComplete_MissingField_ReturnsFalse(string? host, string? id, string? secret)
    {
        var cfg = new CliConfig
        {
            Host = host,
            ClientId = id,
            ClientSecret = secret,
        };
        Assert.False(cfg.IsComplete);
    }

    // ── Environment variable precedence ──────────────────────────────────────

    [Fact]
    public void Load_AllEnvVarsSet_ReturnsEnvValuesWithoutReadingFile()
    {
        Store.Save(
            new CliConfig
            {
                Host = "https://from-file.com",
                ClientId = "file-id",
                ClientSecret = "file-secret",
            }
        );

        Environment.SetEnvironmentVariable("UMBRACO_HOST", "https://from-env.com");
        Environment.SetEnvironmentVariable("UMBRACO_CLIENT_ID", "env-id");
        Environment.SetEnvironmentVariable("UMBRACO_CLIENT_SECRET", "env-secret");
        try
        {
            var loaded = Store.Load();
            Assert.Equal("https://from-env.com", loaded.Host);
            Assert.Equal("env-id", loaded.ClientId);
            Assert.Equal("env-secret", loaded.ClientSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMBRACO_HOST", null);
            Environment.SetEnvironmentVariable("UMBRACO_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("UMBRACO_CLIENT_SECRET", null);
        }
    }

    [Fact]
    public void Load_PartialEnvVars_MergesWithFileValues()
    {
        Store.Save(
            new CliConfig
            {
                Host = "https://from-file.com",
                ClientId = "file-id",
                ClientSecret = "file-secret",
            }
        );

        Environment.SetEnvironmentVariable("UMBRACO_HOST", "https://from-env.com");
        try
        {
            var loaded = Store.Load();
            Assert.Equal("https://from-env.com", loaded.Host);
            Assert.Equal("file-id", loaded.ClientId);
            Assert.Equal("file-secret", loaded.ClientSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMBRACO_HOST", null);
        }
    }
}
