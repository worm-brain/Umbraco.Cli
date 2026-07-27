using System.Text.Json;

namespace Umbraco.Cli.Infrastructure.Config;

/// <summary>
/// Reads and writes the CLI config, which holds one or more named credential profiles (#64).
/// A single command resolves to one profile (chosen by <c>--profile</c> / <c>UMBRACO_PROFILE</c>
/// / the file's default), with <c>UMBRACO_*</c> environment variables overriding that profile's
/// fields. Secrets are encrypted at rest (#45) and a legacy flat config is migrated into a
/// <c>default</c> profile on load.
/// </summary>
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

    /// <summary>
    /// Resolves the effective credentials for a command: the selected profile's values with the
    /// <c>UMBRACO_*</c> environment variables overriding each field. The profile is chosen by
    /// <paramref name="profileName"/>, then <c>UMBRACO_PROFILE</c>, then the file's default,
    /// then <c>default</c>.
    /// </summary>
    /// <param name="profileName">An explicitly requested profile (from <c>--profile</c>), or null.</param>
    /// <returns>The resolved credentials.</returns>
    public CliConfig Load(string? profileName = null)
    {
        var fromEnv = LoadFromEnvironment();
        var file = ReadFile();

        var effectiveName =
            profileName
            ?? Environment.GetEnvironmentVariable("UMBRACO_PROFILE")
            ?? file?.EffectiveDefault
            ?? "default";

        var profile =
            file is not null && file.Profiles.TryGetValue(effectiveName, out var p)
                ? p
                : new CliConfig();

        var fileSecret = SecretProtector.Unprotect(profile.ClientSecret);

        // One-time upgrade of a legacy plaintext secret (issue #45), best-effort.
        if (
            SecretProtector.CanEncrypt
            && !string.IsNullOrEmpty(profile.ClientSecret)
            && !SecretProtector.IsProtected(profile.ClientSecret)
        )
        {
            TryMigrateToEncrypted();
        }

        // Env vars override the selected profile's individual fields (so pure-env / CI still
        // works and env can override one field of a profile).
        return new CliConfig
        {
            Host = fromEnv.Host ?? profile.Host,
            ClientId = fromEnv.ClientId ?? profile.ClientId,
            ClientSecret = fromEnv.ClientSecret ?? fileSecret,
            AllowedCommands = fromEnv.AllowedCommands ?? profile.AllowedCommands,
        };
    }

    /// <summary>
    /// Saves credentials to a named profile, creating the config/profile as needed. The secret
    /// is encrypted at rest; an existing profile's allow-list (#69) is preserved when the saved
    /// config does not carry one. The first profile saved becomes the default.
    /// </summary>
    /// <param name="config">The credentials to store (plaintext secret).</param>
    /// <param name="profileName">The profile to write to; null means the current default.</param>
    public void Save(CliConfig config, string? profileName = null)
    {
        var file = ReadFile() ?? new ConfigFile();
        var name = string.IsNullOrWhiteSpace(profileName) ? file.EffectiveDefault : profileName;

        file.Profiles.TryGetValue(name, out var existing);
        file.Profiles[name] = new CliConfig
        {
            Host = config.Host,
            ClientId = config.ClientId,
            ClientSecret = SecretProtector.Protect(config.ClientSecret),
            // Preserve an existing profile's allow-list unless the caller sets one, so login
            // doesn't strip the guardrail.
            AllowedCommands = config.AllowedCommands ?? existing?.AllowedCommands,
        };

        // The first profile written becomes the default.
        file.DefaultProfile ??= name;

        WriteFile(file);
    }

    /// <summary>Lists the profile names and which one is the default.</summary>
    /// <returns>The profile names and the default profile name.</returns>
    public (IReadOnlyList<string> Names, string Default) ListProfiles()
    {
        var file = ReadFile();
        if (file is null)
            return (Array.Empty<string>(), "default");
        return (
            file.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            file.EffectiveDefault
        );
    }

    /// <summary>Sets the default profile. Returns false when no such profile exists.</summary>
    /// <param name="name">The profile to make default.</param>
    /// <returns>True if switched; false if the profile does not exist.</returns>
    public bool SetDefaultProfile(string name)
    {
        var file = ReadFile();
        if (file is null || !file.Profiles.ContainsKey(name))
            return false;
        file.DefaultProfile = name;
        WriteFile(file);
        return true;
    }

    /// <summary>
    /// Removes a profile (or the default profile when <paramref name="profileName"/> is null).
    /// If the removed profile was the default, another remaining profile becomes the default.
    /// Deletes the whole file once no profiles remain. Returns false when the profile is absent.
    /// </summary>
    /// <param name="profileName">The profile to remove, or null for the default.</param>
    /// <returns>True if a profile was removed.</returns>
    public bool DeleteProfile(string? profileName = null)
    {
        var file = ReadFile();
        if (file is null)
            return false;

        var name = string.IsNullOrWhiteSpace(profileName) ? file.EffectiveDefault : profileName;
        if (!file.Profiles.Remove(name))
            return false;

        if (file.Profiles.Count == 0)
        {
            Delete();
            return true;
        }

        // If we removed the default, pick another remaining profile as the new default.
        if (string.Equals(file.DefaultProfile, name, StringComparison.OrdinalIgnoreCase))
            file.DefaultProfile = file.Profiles.Keys.First();

        WriteFile(file);
        return true;
    }

    /// <summary>Deletes the entire config file.</summary>
    public void Delete()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    /// <summary>
    /// Reads the config file into the profile model, migrating a legacy flat config (host/
    /// clientId/clientSecret at the root) into a single <c>default</c> profile. Returns null when
    /// there is no file or it cannot be read.
    /// </summary>
    /// <returns>The parsed config, or null.</returns>
    private ConfigFile? ReadFile()
    {
        if (!File.Exists(_configPath))
            return null;
        try
        {
            var json = File.ReadAllText(_configPath);
            using var doc = JsonDocument.Parse(json);
            // A profile-shaped file has a "profiles" object; anything else is the legacy flat
            // shape and is migrated into a single default profile.
            if (doc.RootElement.TryGetProperty("profiles", out _))
                return JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions)
                    ?? new ConfigFile();

            var flat = JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
            return new ConfigFile { DefaultProfile = "default", Profiles = { ["default"] = flat } };
        }
        catch
        {
            return null;
        }
    }

    private void WriteFile(ConfigFile file)
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(file, JsonOptions));
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

    /// <summary>
    /// Re-writes the config so any legacy plaintext secrets are encrypted (issue #45).
    /// Best-effort: <see cref="ReadFile"/> returns the stored (plaintext) secrets and
    /// <see cref="WriteFile"/> re-encrypts them via <see cref="Save"/>-style protection is not
    /// used here; instead we re-protect each profile secret. Any write failure is swallowed.
    /// </summary>
    private void TryMigrateToEncrypted()
    {
        try
        {
            var file = ReadFile();
            if (file is null)
                return;
            foreach (var profile in file.Profiles.Values)
                profile.ClientSecret = SecretProtector.Protect(
                    SecretProtector.Unprotect(profile.ClientSecret)
                );
            WriteFile(file);
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
            AllowedCommands = Environment.GetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS"),
        };
}
