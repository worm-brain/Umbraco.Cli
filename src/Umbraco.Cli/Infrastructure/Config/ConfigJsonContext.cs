using System.Text.Json.Serialization;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Infrastructure.Config;

/// <summary>
/// Source-generated <c>System.Text.Json</c> metadata for the config file and the token cache
/// (#425). Every command reads both before its first request, and building the reflection
/// metadata for them on first use cost 36-45 ms per run. The options match what the reflection
/// serializer used (defaults plus indented output), so the files on disk are byte-for-byte what
/// earlier versions wrote and read.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfigFile))]
[JsonSerializable(typeof(CliConfig))]
[JsonSerializable(typeof(Dictionary<string, CachedToken>))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext { }
