using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How many token-endpoint requests a command run on client credentials makes (#408, #248): one
/// when the token cache is cold, however many API requests follow, and none when an earlier run
/// left a fresh token in it. Each <see cref="HttpCli"/> is one process, so two sharing a cache are
/// two runs of <c>umbraco</c> sharing the token file.
/// </summary>
[Collection("ConsoleCapture")]
public class TokenCacheRequestCountTests
{
    /// <summary>A profile holding client credentials for <see cref="HttpCli.Host"/>.</summary>
    private static readonly CliConfig Profile = new()
    {
        Host = HttpCli.Host,
        ClientId = "umbraco-back-office-ci",
        ClientSecret = "secret",
    };

    [Fact]
    public async Task ContentList_ColdTokenCache_ExchangesTheCredentialsOnce()
    {
        // Arrange
        var cli = new HttpCli(SiteWithDocuments(10), Profile, new MemoryTokenCache());

        // Act
        var run = await cli.RunWithClientCredentialsAsync("content list");

        // Assert
        run.HasRequestCount(
            FakeUmbraco.IsTokenRequest,
            "token-endpoint",
            1,
            "1 exchange: the cache holds no token"
        );
    }

    [Fact]
    public async Task ContentListAll_ColdTokenCache_ExchangesTheCredentialsOnceForEveryPage()
    {
        // Arrange: three pages of documents, so the run makes several API requests.
        var cli = new HttpCli(SiteWithDocuments(250), Profile, new MemoryTokenCache());

        // Act
        var run = await cli.RunWithClientCredentialsAsync("content list --all");

        // Assert
        run.HasRequestCount(
            FakeUmbraco.IsTokenRequest,
            "token-endpoint",
            1,
            "1 exchange for the run, not one per API request"
        );
    }

    [Fact]
    public async Task ContentList_WarmTokenCache_MakesNoTokenRequest()
    {
        // Arrange: an earlier run fills the shared cache.
        var handler = SiteWithDocuments(10);
        var cache = new MemoryTokenCache();
        await new HttpCli(handler, Profile, cache).RunWithClientCredentialsAsync("content list");

        // Act
        var run = await new HttpCli(handler, Profile, cache).RunWithClientCredentialsAsync(
            "content list"
        );

        // Assert
        run.HasRequestCount(
            FakeUmbraco.IsTokenRequest,
            "token-endpoint",
            0,
            "no exchange: the earlier run's token is still fresh"
        );
    }

    /// <summary>An instance holding <paramref name="count"/> documents at the root, all of one type.</summary>
    /// <param name="count">How many documents.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler SiteWithDocuments(int count)
    {
        var type = Guid.NewGuid();
        var documents = new FakeTree();
        documents.AddMany(count, null, i => FakeUmbraco.DocumentItem($"Page {i}", type));
        return FakeUmbraco.ContentSite(documents);
    }

    /// <summary>The token file held in memory, shared by the runs of one test.</summary>
    private sealed class MemoryTokenCache : ITokenCache
    {
        private readonly Dictionary<string, CachedToken> _entries = [];

        /// <inheritdoc />
        public CachedToken? Read(string key) => _entries.GetValueOrDefault(key);

        /// <inheritdoc />
        public void Write(string key, CachedToken token) => _entries[key] = token;

        /// <inheritdoc />
        public void Remove(string key) => _entries.Remove(key);

        /// <inheritdoc />
        public void RemoveByPrefix(string prefix)
        {
            foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix)).ToList())
                _entries.Remove(key);
        }
    }
}
