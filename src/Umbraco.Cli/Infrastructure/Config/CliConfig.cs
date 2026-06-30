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

    public bool IsComplete =>
        !string.IsNullOrEmpty(Host)
        && !string.IsNullOrEmpty(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);
}
