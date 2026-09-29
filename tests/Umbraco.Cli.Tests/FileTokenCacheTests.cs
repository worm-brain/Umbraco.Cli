using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The on-disk token cache (#248): tokens survive between instances (processes), expired ones are
/// dropped, and a broken file is an empty cache rather than an error.
/// </summary>
public sealed class FileTokenCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tc-{Guid.NewGuid():N}");

    private string CachePath => Path.Combine(_dir, "token-cache.json");

    /// <summary>A clock fixed at a known instant.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private FileTokenCache Cache() => new(CachePath, new FixedClock(Now));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RemoveByPrefix_DropsMatchingKeysOnly()
    {
        var token = new CachedToken("abc", Now.AddMinutes(4));
        Cache().Write("https://a|client|one", token);
        Cache().Write("https://a|client-2|one", token);

        Cache().RemoveByPrefix("https://a|client|");

        Assert.Equal(
            (null, token),
            (Cache().Read("https://a|client|one"), Cache().Read("https://a|client-2|one"))
        );
    }

    [Fact]
    public void Read_AfterWriteFromAnotherInstance_ReturnsTheToken()
    {
        var token = new CachedToken("abc", Now.AddMinutes(4));
        Cache().Write("key", token);

        Assert.Equal(token, Cache().Read("key"));
    }

    [Fact]
    public void Read_NoFile_ReturnsNull()
    {
        Assert.Null(Cache().Read("key"));
    }

    [Fact]
    public void Read_CorruptFile_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CachePath, "{ not json");

        Assert.Null(Cache().Read("key"));
    }

    [Fact]
    public void Write_DropsExpiredEntries()
    {
        var cache = Cache();
        cache.Write("old", new CachedToken("stale", Now.AddMinutes(-1)));

        cache.Write("new", new CachedToken("fresh", Now.AddMinutes(4)));

        Assert.Null(cache.Read("old"));
    }

    [Fact]
    public void Remove_DeletesTheEntry()
    {
        var cache = Cache();
        cache.Write("key", new CachedToken("abc", Now.AddMinutes(4)));

        cache.Remove("key");

        Assert.Null(cache.Read("key"));
    }

    [Fact]
    public void Write_FileBytes_MatchWhatTheReflectionSerializerWrote()
    {
        // #425: the cache moved to source-generated JSON; a file written by either must be the
        // same bytes, so older and newer CLI builds keep sharing one cache.
        var token = new CachedToken("abc", Now.AddMinutes(4));
        Cache().Write("key", token);
        var reflectionWritten = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, CachedToken> { ["key"] = token },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );

        Assert.Equal(reflectionWritten, File.ReadAllText(CachePath));
    }

    [Fact]
    public void Read_FileAnEarlierVersionWrote_ReturnsTheToken()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            CachePath,
            """
            {
              "key": {
                "AccessToken": "abc",
                "RefreshAt": "2026-09-25T12:04:00+00:00"
              }
            }
            """
        );

        Assert.Equal(new CachedToken("abc", Now.AddMinutes(4)), Cache().Read("key"));
    }

    [Fact]
    public void Read_FileWithTheWrongShape_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CachePath, """{ "key": "not a token" }""");

        Assert.Null(Cache().Read("key"));
    }

    [Fact]
    public void Write_LeavesNoTempFileBehind()
    {
        Cache().Write("key", new CachedToken("abc", Now.AddMinutes(4)));

        Assert.Equal(["token-cache.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
