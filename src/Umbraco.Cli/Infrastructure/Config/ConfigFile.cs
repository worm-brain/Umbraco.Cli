using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Config;

/// <summary>
/// The on-disk config document (#64): a set of named credential profiles plus which one is the
/// default. Each profile is a <see cref="CliConfig"/> (host + client credentials + allow-list).
/// A legacy flat config (host/clientId/clientSecret at the root) is migrated into a single
/// <c>default</c> profile on load.
/// </summary>
public sealed class ConfigFile
{
    /// <summary>The name of the profile used when none is requested. Defaults to <c>default</c>.</summary>
    [JsonPropertyName("defaultProfile")]
    public string? DefaultProfile { get; set; }

    /// <summary>The named profiles, keyed by profile name.</summary>
    [JsonPropertyName("profiles")]
    public Dictionary<string, CliConfig> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The default profile name, falling back to <c>default</c> when unset. Computed, so never
    /// written to the file (#380). A file an older version wrote with it still loads: a property
    /// with no setter is not read back.
    /// </summary>
    [JsonIgnore]
    public string EffectiveDefault =>
        string.IsNullOrWhiteSpace(DefaultProfile) ? "default" : DefaultProfile;
}
