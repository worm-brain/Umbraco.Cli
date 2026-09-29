using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Config;

/// <summary>
/// One credential profile in the config file: the Umbraco host, the API user's client
/// credentials and an optional command allow-list.
/// </summary>
public sealed class CliConfig
{
    /// <summary>The Umbraco base URL.</summary>
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    /// <summary>The API user's client id.</summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    /// <summary>The API user's client secret.</summary>
    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional comma-separated allow-list restricting which commands may run (#69): a noun
    /// group ("content") or a specific command ("content.list"). Null means no restriction from
    /// this profile; a present but blank value is an explicit lockdown. The list applies to every
    /// profile in the same file, and alongside UMBRACO_ALLOWED_COMMANDS, so it can only tighten
    /// (SEC-PRIV-002; see <see cref="ConfigStore.AllowLists"/>).
    /// </summary>
    [JsonPropertyName("allowedCommands")]
    public string? AllowedCommands { get; set; }

    /// <summary>
    /// Whether the profile has a host, client id and client secret, i.e. enough to authenticate.
    /// Computed, so never written to the file (#380). A file an older version wrote with it still
    /// loads: a property with no setter is not read back.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrEmpty(Host)
        && !string.IsNullOrEmpty(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);
}
