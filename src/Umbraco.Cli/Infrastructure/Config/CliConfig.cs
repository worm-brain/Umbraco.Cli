using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Config;

public sealed class CliConfig
{
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional comma-separated allow-list restricting which commands may run (#69): a noun
    /// group ("content") or a specific command ("content.list"). Null/empty means no
    /// restriction. Overridden by the UMBRACO_ALLOWED_COMMANDS environment variable.
    /// </summary>
    [JsonPropertyName("allowedCommands")]
    public string? AllowedCommands { get; set; }

    public bool IsComplete =>
        !string.IsNullOrEmpty(Host)
        && !string.IsNullOrEmpty(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);
}
