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
            Blank(profileName)
            ?? Blank(Environment.GetEnvironmentVariable("UMBRACO_PROFILE"))
            ?? file?.EffectiveDefault
            ?? "default";

        var profile =
            file is not null && file.Profiles.TryGetValue(effectiveName, out var p)
                ? p
                : new CliConfig();

        // Never let an undecryptable secret (a config copied from another machine/user, or a
        // truncated blob) crash the whole CLI — treat it as a missing secret.
        string? fileSecret;
        try
        {
            fileSecret = SecretProtector.Unprotect(profile.ClientSecret);
        }
        catch
        {
            fileSecret = null;
        }

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
        // Never overwrite a config file we couldn't parse — that would silently wipe every
        // other profile's credentials. Fail loudly instead.
        if (FileExistsButUnreadable())
            throw new InvalidOperationException(
                $"The config file at '{_configPath}' exists but could not be read; refusing to "
                    + "overwrite it. Fix or remove it, then retry."
            );

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
        {
            // A corrupt/unreadable file with no specific profile requested: remove it wholesale
            // so credentials never linger on a failed logout (matches the old logout semantics).
            if (string.IsNullOrWhiteSpace(profileName) && File.Exists(_configPath))
            {
                Delete();
                return true;
            }
            return false;
        }

        var wasDefault = string.Equals(
            string.IsNullOrWhiteSpace(profileName) ? file.EffectiveDefault : profileName,
            file.EffectiveDefault,
            StringComparison.OrdinalIgnoreCase
        );
        var name = string.IsNullOrWhiteSpace(profileName) ? file.EffectiveDefault : profileName;
        if (!file.Profiles.Remove(name))
            return false;

        if (file.Profiles.Count == 0)
        {
            Delete();
            return true;
        }

        // If we removed the default, pick another remaining profile as the new default.
        if (wasDefault)
            file.DefaultProfile = file.Profiles.Keys.First();

        WriteFile(file);
        return true;
    }

    /// <summary>The outcome of a <see cref="Logout"/> call, so the caller can report accurately.</summary>
    public enum LogoutOutcome
    {
        /// <summary>No matching profile (or file) was found; nothing changed.</summary>
        NothingToRemove,

        /// <summary>The profile (and, if it was the last, the file) was removed.</summary>
        Removed,

        /// <summary>Credentials were cleared but the profile's allow-list was preserved (#83).</summary>
        CredentialsClearedAllowListKept,
    }

    /// <summary>
    /// Logs out of a profile (the default when <paramref name="profileName"/> is null). If the
    /// profile carries a command allow-list (#69), only its credentials are cleared and the
    /// allow-list is preserved (#83 H2) — so a restricted caller cannot drop the guardrail by
    /// logging out (logout bypasses the allow-list gate). Otherwise the profile is removed
    /// entirely (deleting the file when it was the last profile), matching the old semantics.
    ///
    /// When the preserved stub was the default profile it stays the default (deliberately, unlike
    /// <see cref="DeleteProfile"/> which reassigns): the guardrail lives on that profile, so the
    /// next command still resolves to it and enforces the allow-list (aborting at auth until a
    /// re-login, rather than silently falling through to an unrestricted profile).
    /// </summary>
    /// <param name="profileName">The profile to log out of, or null for the default.</param>
    /// <returns>What happened, so the command can report it.</returns>
    public LogoutOutcome Logout(string? profileName = null)
    {
        var file = ReadFile();
        if (file is null)
        {
            // Corrupt/unreadable file with no specific profile: remove it wholesale so
            // credentials never linger on a failed logout (matches DeleteProfile).
            if (string.IsNullOrWhiteSpace(profileName) && File.Exists(_configPath))
            {
                Delete();
                return LogoutOutcome.Removed;
            }
            return LogoutOutcome.NothingToRemove;
        }

        var name = string.IsNullOrWhiteSpace(profileName) ? file.EffectiveDefault : profileName;
        if (!file.Profiles.TryGetValue(name, out var profile))
            return LogoutOutcome.NothingToRemove;

        if (!string.IsNullOrWhiteSpace(profile.AllowedCommands))
        {
            // Keep the allow-list as a credential-less stub so the guardrail survives logout.
            file.Profiles[name] = new CliConfig { AllowedCommands = profile.AllowedCommands };
            WriteFile(file);
            return LogoutOutcome.CredentialsClearedAllowListKept;
        }

        return DeleteProfile(profileName) ? LogoutOutcome.Removed : LogoutOutcome.NothingToRemove;
    }

    /// <summary>Whether the config file exists on disk but cannot be parsed (fail-closed signal, #83 M1).</summary>
    /// <returns>True when a file is present but unreadable.</returns>
    public bool FileExistsButUnreadable() => File.Exists(_configPath) && ReadFile() is null;

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
            ConfigFile file;
            if (doc.RootElement.TryGetProperty("profiles", out _))
            {
                file =
                    JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions) ?? new ConfigFile();
            }
            else
            {
                var flat =
                    JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
                file = new ConfigFile
                {
                    DefaultProfile = "default",
                    Profiles = { ["default"] = flat },
                };
            }

            // System.Text.Json replaces the field initializer with a case-SENSITIVE dictionary
            // on deserialize, so re-wrap to keep profile names case-insensitive throughout.
            file.Profiles = new Dictionary<string, CliConfig>(
                file.Profiles,
                StringComparer.OrdinalIgnoreCase
            );
            return file;
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

        // Write atomically: serialize to a temp file next to the target, then replace/move it in
        // one step. A crash or a concurrent invocation can no longer truncate the file and lose
        // every profile's credentials mid-write.
        var tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
        RestrictPermissions(tempPath);
        if (File.Exists(_configPath))
            File.Replace(tempPath, _configPath, destinationBackupFileName: null);
        else
            File.Move(tempPath, _configPath);
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
        catch
        {
            // Purely opportunistic re-encryption: swallow everything (write failures, an
            // undecryptable secret from another machine, etc.) so it never breaks a command.
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

    /// <summary>Whether a config file exists and defines the given profile.</summary>
    /// <param name="name">The profile name.</param>
    /// <returns>True if the profile exists.</returns>
    public bool HasProfile(string name) => ReadFile()?.Profiles.ContainsKey(name) ?? false;

    /// <summary>Whether a readable config file defines any profiles.</summary>
    public bool HasAnyProfiles => ReadFile()?.Profiles.Count > 0;

    /// <summary>Returns null for a null/whitespace string, else the string itself.</summary>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
