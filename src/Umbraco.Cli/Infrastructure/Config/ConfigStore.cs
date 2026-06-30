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

            // Env vars override individual file values.
            return new CliConfig
            {
                Host = fromEnv.Host ?? fromFile.Host,
                ClientId = fromEnv.ClientId ?? fromFile.ClientId,
                ClientSecret = fromEnv.ClientSecret ?? fromFile.ClientSecret,
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
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    public void Delete()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    private static CliConfig LoadFromEnvironment() =>
        new()
        {
            Host = Environment.GetEnvironmentVariable("UMBRACO_HOST"),
            ClientId = Environment.GetEnvironmentVariable("UMBRACO_CLIENT_ID"),
            ClientSecret = Environment.GetEnvironmentVariable("UMBRACO_CLIENT_SECRET"),
        };
}
