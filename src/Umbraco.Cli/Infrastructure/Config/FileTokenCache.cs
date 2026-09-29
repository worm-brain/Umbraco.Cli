using System.Text.Json;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Infrastructure.Config;

/// <summary>
/// Keeps client-credentials tokens in a per-user file between CLI runs (#248). Without it every
/// command exchanged credentials again - about 940 token requests in one test session - which
/// doubled the requests per command and filled the site's log.
/// <para>
/// The file is <c>Umbraco/token-cache.json</c> under the per-user local app-data folder (not the
/// roaming one the config lives in: a token is machine-local and short-lived). It holds a bearer
/// token that is valid for about five minutes, so it is protected the way the user chose for
/// that (#248 scope decision, 2026-09-25): on Unix the file is created mode 600 inside a mode-700
/// directory, never briefly readable by others; on Windows it sits in the user's profile, whose
/// ACL already limits it to that user. Keys carry no secret (see
/// <see cref="UmbracoAuthService"/>). <c>UMBRACO_NO_TOKEN_CACHE=1</c> turns it off.
/// </para>
/// <para>
/// Every failure is soft, as <see cref="ITokenCache"/> requires: an unreadable file is an empty
/// cache and a failed write is dropped. Concurrent runs each write the whole file atomically, so
/// the worst a race does is lose another run's entry, which costs one extra token request.
/// </para>
/// </summary>
public sealed class FileTokenCache : ITokenCache
{
    private readonly string _path;
    private readonly TimeProvider _time;

    /// <summary>The default cache file path.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Umbraco",
            "token-cache.json"
        );

    /// <summary>Creates a cache backed by <paramref name="path"/>.</summary>
    /// <param name="path">The cache file; <see cref="DefaultPath"/> when null.</param>
    /// <param name="timeProvider">The clock used to prune expired entries; the system clock when null.</param>
    public FileTokenCache(string? path = null, TimeProvider? timeProvider = null)
    {
        _path = path ?? DefaultPath;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// The cache to use for this process: the file cache, or null when
    /// <c>UMBRACO_NO_TOKEN_CACHE</c> is truthy.
    /// </summary>
    /// <returns>The cache, or null for none.</returns>
    public static ITokenCache? FromEnvironment() =>
        EnvironmentFlags.IsOn("UMBRACO_NO_TOKEN_CACHE") ? null : new FileTokenCache();

    /// <inheritdoc />
    public CachedToken? Read(string key) => Load().GetValueOrDefault(key);

    /// <inheritdoc />
    public void Write(string key, CachedToken token)
    {
        var entries = Load();
        entries[key] = token;
        Save(entries);
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        var entries = Load();
        if (entries.Remove(key))
            Save(entries);
    }

    /// <inheritdoc />
    public void RemoveByPrefix(string prefix)
    {
        var entries = Load();
        var doomed = entries
            .Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var key in doomed)
            entries.Remove(key);
        if (doomed.Count > 0)
            Save(entries);
    }

    private Dictionary<string, CachedToken> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize(
                    File.ReadAllText(_path),
                    ConfigJsonContext.Default.DictionaryStringCachedToken
                ) ?? [];
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Save(Dictionary<string, CachedToken> entries)
    {
        // Expired entries are dropped on every write, so the file holds only tokens that could
        // still be used and does not grow with every host ever visited.
        var now = _time.GetUtcNow();
        var live = entries.Where(e => e.Value.RefreshAt > now).ToDictionary();

        // A unique temp name: two runs writing at once must not write into the same temp file.
        var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temp, CreateOptions()))
                JsonSerializer.Serialize(
                    stream,
                    live,
                    ConfigJsonContext.Default.DictionaryStringCachedToken
                );
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dropped: the next run authenticates again. Nothing is left half-written.
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Options that create the file owner-only from the first byte on Unix (mode 600). Setting the
    /// mode after writing, as the config store does, would leave a window in which the token is
    /// readable by others.
    /// </summary>
    private static FileStreamOptions CreateOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private static void CreateDirectory(string dir)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(dir);
        else
            Directory.CreateDirectory(
                dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
