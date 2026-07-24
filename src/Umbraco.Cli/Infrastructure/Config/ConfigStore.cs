using System.Text.Json;

namespace Umbraco.Cli.Infrastructure.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Umbraco",
            "config.json"
        );

    private readonly string _configPath;

    public ConfigStore(string? configPath = null)
    {
        _configPath = configPath ?? DefaultConfigPath;
    }

    /// <summary>
    /// Returns a store rooted at <paramref name="path"/> when one is supplied (honouring
    /// <c>--config</c>), otherwise the <paramref name="fallback"/> store (default path / env vars).
    /// </summary>
    public static ConfigStore Resolve(string? path, ConfigStore fallback) =>
        string.IsNullOrEmpty(path) ? fallback : new ConfigStore(path);

    public CliConfig Load()
    {
        // Environment variables take precedence over the config file.
        var fromEnv = LoadFromEnvironment();
        if (fromEnv.IsComplete)
            return fromEnv;

        if (!File.Exists(_configPath))
            return fromEnv;

        try
        {
            var json = File.ReadAllText(_configPath);
            var fromFile =
                JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();

            // The stored secret is encrypted at rest on Windows (issue #45); decrypt it
            // back to plaintext for use. Legacy plaintext files are passed through
            // unchanged by SecretProtector.
            var fileSecret = SecretProtector.Unprotect(fromFile.ClientSecret);

            // One-time upgrade: a config written before encryption was added (plaintext
            // secret, no dpapi: prefix) is transparently re-saved encrypted so existing
            // users are protected without re-running `auth login` (issue #45). Best-effort
            // and only where encryption is actually available.
            if (
                SecretProtector.CanEncrypt
                && !string.IsNullOrEmpty(fromFile.ClientSecret)
                && !SecretProtector.IsProtected(fromFile.ClientSecret)
            )
            {
                TryMigrateToEncrypted(fromFile);
            }

            // Env vars override individual file values.
            return new CliConfig
            {
                Host = fromEnv.Host ?? fromFile.Host,
                ClientId = fromEnv.ClientId ?? fromFile.ClientId,
                ClientSecret = fromEnv.ClientSecret ?? fileSecret,
            };
        }
        catch
        {
            return fromEnv;
        }
    }

    public void Save(CliConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);

        // Encrypt the secret at rest (issue #45) without mutating the caller's instance.
        var toPersist = new CliConfig
        {
            Host = config.Host,
            ClientId = config.ClientId,
            ClientSecret = SecretProtector.Protect(config.ClientSecret),
        };

        File.WriteAllText(_configPath, JsonSerializer.Serialize(toPersist, JsonOptions));
        RestrictPermissions(_configPath);
    }

    /// <summary>
    /// Restricts the config file to the owner. On Unix this sets mode 600 (issue #45); on
    /// Windows the secret is already DPAPI-encrypted per user, so no ACL change is made.
    /// Best-effort: permission failures are swallowed so login still succeeds.
    /// </summary>
    /// <param name="path">The config file path.</param>
    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the file is still written, just without tightened permissions.
        }
    }

    public void Delete()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    /// <summary>
    /// Re-saves a config whose secret is stored in legacy plaintext, encrypting it in the
    /// process (issue #45). Best-effort: the value read from <paramref name="fromFile"/> is
    /// still plaintext at this point, so a normal <see cref="Save"/> re-encrypts it. Any
    /// write failure is swallowed so a read-only config directory never breaks a command.
    /// </summary>
    /// <param name="fromFile">The config just read from disk, with a plaintext secret.</param>
    private void TryMigrateToEncrypted(CliConfig fromFile)
    {
        try
        {
            Save(fromFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the file stays as-is (still readable via the legacy path).
        }
    }

    private static CliConfig LoadFromEnvironment() =>
        new()
        {
            Host = Environment.GetEnvironmentVariable("UMBRACO_HOST"),
            ClientId = Environment.GetEnvironmentVariable("UMBRACO_CLIENT_ID"),
            ClientSecret = Environment.GetEnvironmentVariable("UMBRACO_CLIENT_SECRET"),
        };
}
