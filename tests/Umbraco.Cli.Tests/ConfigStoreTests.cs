using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

// Shares the "ConsoleCapture" collection with the command tests: this class mutates the
// process-global UMBRACO_ALLOWED_COMMANDS env var mid-test, which CommandContextFactory reads
// when resolving the allow-list. Serialising with the command tests prevents that write from
// racing a concurrent command run (which would spuriously block an allowed command).
[Collection("ConsoleCapture")]
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
        Environment.SetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS", null);
        Environment.SetEnvironmentVariable("UMBRACO_PROFILE", null);
    }

    [Fact]
    public void SaveThenLoad_PreservesAllowedCommands()
    {
        // #69 guard: Save must round-trip the allow-list, or login / the legacy-secret re-save
        // would silently strip the guardrail from disk.
        Store.Save(
            new CliConfig
            {
                Host = "https://example.umbraco.io",
                ClientId = "id",
                ClientSecret = "secret",
                AllowedCommands = "content,media.list",
            }
        );

        Assert.Equal("content,media.list", Store.Load().AllowedCommands);
    }

    [Fact]
    public void Load_AllowedCommandsFromEnv_TakesPrecedence()
    {
        // Env overrides the file value (#69).
        Store.Save(
            new CliConfig
            {
                Host = "https://h",
                ClientId = "id",
                ClientSecret = "secret",
                AllowedCommands = "file-value",
            }
        );
        Environment.SetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS", "content,media");

        Assert.Equal("content,media", Store.Load().AllowedCommands);
    }

    // ── Named profiles (#64) ────────────────────────────────────────────────

    private static CliConfig Creds(string host) =>
        new()
        {
            Host = host,
            ClientId = "id",
            ClientSecret = "secret",
        };

    [Fact]
    public void Save_NamedProfiles_LoadEachByName()
    {
        Store.Save(Creds("https://default"));
        Store.Save(Creds("https://prod"), "prod");

        Assert.Equal("https://default", Store.Load().Host); // default profile
        Assert.Equal("https://prod", Store.Load("prod").Host);
        Assert.Equal("https://default", Store.Load("default").Host);
    }

    [Fact]
    public void FirstProfileSaved_BecomesDefault()
    {
        Store.Save(Creds("https://first"), "prod");

        Assert.Equal("https://first", Store.Load().Host); // default resolves to the only profile
        var (names, def) = Store.ListProfiles();
        Assert.Equal("prod", def);
        Assert.Contains("prod", names);
    }

    [Fact]
    public void SetDefaultProfile_SwitchesWhichLoadReturns()
    {
        Store.Save(Creds("https://default"));
        Store.Save(Creds("https://prod"), "prod");

        Assert.True(Store.SetDefaultProfile("prod"));
        Assert.Equal("https://prod", Store.Load().Host);
        Assert.Equal("prod", Store.ListProfiles().Default);
    }

    [Fact]
    public void SetDefaultProfile_UnknownProfile_ReturnsFalse() =>
        Assert.False(Store.SetDefaultProfile("nope"));

    [Fact]
    public void Logout_ProfileWithAllowList_PreservesAllowListAndClearsCredentials()
    {
        // #83 H2: logout must not silently drop a file-based allow-list. The profile is kept as a
        // credential-less stub so the guardrail survives (logout bypasses the allow-list gate).
        Store.Save(
            new CliConfig
            {
                Host = "https://h",
                ClientId = "id",
                ClientSecret = "secret",
                AllowedCommands = "content,media",
            }
        );

        var outcome = Store.Logout();

        Assert.Equal(ConfigStore.LogoutOutcome.CredentialsClearedAllowListKept, outcome);
        var after = Store.Load();
        Assert.Equal("content,media", after.AllowedCommands); // guardrail preserved
        Assert.Null(after.Host); // credentials cleared
        Assert.Null(after.ClientId);
    }

    [Fact]
    public void Logout_ProfileWithoutAllowList_RemovesProfile()
    {
        // With no allow-list to preserve, logout removes the profile entirely (old semantics).
        Store.Save(
            new CliConfig
            {
                Host = "https://h",
                ClientId = "id",
                ClientSecret = "secret",
            }
        );

        var outcome = Store.Logout();

        Assert.Equal(ConfigStore.LogoutOutcome.Removed, outcome);
        Assert.False(Store.HasAnyProfiles);
    }

    [Fact]
    public void FileExistsButUnreadable_CorruptFile_IsTrue()
    {
        // #83 M1: a present-but-unparseable config is detectable so the caller can warn rather
        // than fail open silently.
        File.WriteAllText(_tempPath, "{ this is not valid json ");

        Assert.True(Store.FileExistsButUnreadable());
    }

    [Fact]
    public void FileExistsButUnreadable_ValidFile_IsFalse()
    {
        Store.Save(
            new CliConfig
            {
                Host = "https://h",
                ClientId = "id",
                ClientSecret = "s",
            }
        );

        Assert.False(Store.FileExistsButUnreadable());
    }

    [Fact]
    public void DeleteProfile_RemovesOneAndReassignsDefault()
    {
        Store.Save(Creds("https://default"));
        Store.Save(Creds("https://prod"), "prod");
        Store.SetDefaultProfile("prod");

        Assert.True(Store.DeleteProfile("prod"));
        var (names, def) = Store.ListProfiles();
        Assert.DoesNotContain("prod", names);
        Assert.Equal("default", def); // reassigned to the remaining profile
    }

    [Fact]
    public void DeleteProfile_LastProfile_RemovesFile()
    {
        Store.Save(Creds("https://only"));
        Assert.True(Store.DeleteProfile());
        Assert.False(File.Exists(_tempPath));
    }

    [Fact]
    public void Load_EnvOverridesSelectedProfileField()
    {
        Store.Save(Creds("https://prod"), "prod");
        Environment.SetEnvironmentVariable("UMBRACO_HOST", "https://env-override");

        // Env overrides the selected profile's host, but the profile supplies id/secret.
        var loaded = Store.Load("prod");
        Assert.Equal("https://env-override", loaded.Host);
        Assert.Equal("id", loaded.ClientId);
    }

    [Fact]
    public void Load_UmbracoProfileEnv_SelectsProfile()
    {
        Store.Save(Creds("https://default"));
        Store.Save(Creds("https://staging"), "staging");
        Environment.SetEnvironmentVariable("UMBRACO_PROFILE", "staging");

        Assert.Equal("https://staging", Store.Load().Host);
    }

    [Fact]
    public void LegacyFlatConfig_LoadsAsDefaultProfile()
    {
        // A pre-profiles flat file must still resolve (migrated to a 'default' profile).
        File.WriteAllText(
            _tempPath,
            """{"host":"https://legacy","clientId":"id","clientSecret":"secret"}"""
        );

        Assert.Equal("https://legacy", Store.Load().Host);
        Assert.Contains("default", Store.ListProfiles().Names);
    }

    [Fact]
    public void Profiles_AreCaseInsensitiveAcrossReload()
    {
        // Regression for the JSON round-trip dropping the case-insensitive comparer.
        Store.Save(Creds("https://prod"), "prod");

        Assert.Equal("https://prod", Store.Load("Prod").Host);
        Assert.True(Store.SetDefaultProfile("PROD"));
        // Saving under a differently-cased name overwrites, not duplicates.
        Store.Save(Creds("https://prod2"), "Prod");
        Assert.Single(Store.ListProfiles().Names);
    }

    [Fact]
    public void Load_UndecryptableSecret_DoesNotThrow()
    {
        // A dpapi: blob copied from another machine/user can't be decrypted; Load must degrade
        // (treat the secret as missing) rather than crash every command.
        File.WriteAllText(
            _tempPath,
            """{"profiles":{"default":{"host":"https://h","clientId":"id","clientSecret":"dpapi:AAAAnotvalid"}}}"""
        );

        var loaded = Store.Load();
        Assert.Equal("https://h", loaded.Host);
        Assert.True(string.IsNullOrEmpty(loaded.ClientSecret));
    }

    [Fact]
    public void Save_PreservesExistingProfileAllowList()
    {
        // #69 guard: re-saving credentials (login) must not strip a profile's allow-list.
        Store.Save(
            new CliConfig
            {
                Host = "https://h",
                ClientId = "id",
                ClientSecret = "secret",
                AllowedCommands = "content,media",
            },
            "prod"
        );
        Store.Save(Creds("https://h2"), "prod"); // re-login, no allow-list supplied

        Assert.Equal("content,media", Store.Load("prod").AllowedCommands);
    }

    [Fact]
    public void Save_DoesNotStealExplicitDefault()
    {
        Store.Save(Creds("https://default"));
        Store.Save(Creds("https://prod"), "prod");
        Store.SetDefaultProfile("prod");

        // Saving another profile must not change the explicitly-chosen default.
        Store.Save(Creds("https://stage"), "stage");
        Assert.Equal("prod", Store.ListProfiles().Default);
    }

    [Fact]
    public void Save_OverUnreadableFile_Throws()
    {
        File.WriteAllText(_tempPath, "{ this is not valid json");
        Assert.Throws<InvalidOperationException>(() => Store.Save(Creds("https://h")));
    }

    [Fact]
    public void Load_FileAllowList_HonouredWhenAuthFullyFromEnv()
    {
        // A file-only allow-list must survive even when auth comes entirely from env (the
        // env-complete short-circuit must still read it) - otherwise the guardrail fails open.
        Store.Save(
            new CliConfig
            {
                Host = "https://file-host",
                ClientId = "file-id",
                ClientSecret = "file-secret",
                AllowedCommands = "content",
            }
        );
        Environment.SetEnvironmentVariable("UMBRACO_HOST", "https://env-host");
        Environment.SetEnvironmentVariable("UMBRACO_CLIENT_ID", "env-id");
        Environment.SetEnvironmentVariable("UMBRACO_CLIENT_SECRET", "env-secret");

        var loaded = Store.Load();

        Assert.Equal("https://env-host", loaded.Host); // env auth won
        Assert.Equal("content", loaded.AllowedCommands); // but the file allow-list survived
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
    public void Save_OnWindows_DoesNotPersistSecretAsPlaintext()
    {
        // Regression for #45: on Windows the secret must be DPAPI-encrypted at rest, so the
        // raw file must not contain the cleartext value.
        if (!OperatingSystem.IsWindows())
            return; // encryption is Windows-only; other platforms rely on file permissions

        Store.Save(
            new CliConfig
            {
                Host = "https://example.com",
                ClientId = "id",
                ClientSecret = "super-secret-value",
            }
        );

        var raw = File.ReadAllText(_tempPath);
        Assert.DoesNotContain("super-secret-value", raw);
        Assert.Contains("dpapi:", raw);

        // ...and it still round-trips back to the original plaintext.
        Assert.Equal("super-secret-value", Store.Load().ClientSecret);
    }

    [Fact]
    public void Load_LegacyPlaintextSecret_StillReads()
    {
        // Backward compatibility for #45: a config written before encryption (plain
        // clientSecret, no dpapi: prefix) must still load.
        File.WriteAllText(
            _tempPath,
            """{"host":"https://example.com","clientId":"id","clientSecret":"legacy-plain"}"""
        );

        Assert.Equal("legacy-plain", Store.Load().ClientSecret);
    }

    [Fact]
    public void Load_OnWindows_MigratesLegacyPlaintextSecretToEncrypted()
    {
        // Regression for #45: loading a pre-encryption config must transparently re-save
        // it encrypted so existing users are protected without re-running auth login.
        if (!OperatingSystem.IsWindows())
            return; // migration only encrypts on Windows

        File.WriteAllText(
            _tempPath,
            """{"host":"https://example.com","clientId":"id","clientSecret":"legacy-plain"}"""
        );

        var loaded = Store.Load();

        // Load still returns the usable plaintext...
        Assert.Equal("legacy-plain", loaded.ClientSecret);
        // ...but the file on disk has been upgraded to encrypted form.
        var raw = File.ReadAllText(_tempPath);
        Assert.DoesNotContain("legacy-plain", raw);
        Assert.Contains("dpapi:", raw);
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
