using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>auth logout</c> leaves no usable credential for the profile on disk (#260): the stored
/// credentials go, and so does every cached access token for the profile's host and client id.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class LogoutCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"logout-{Guid.NewGuid():N}");

    private string CachePath => Path.Combine(_dir, "token-cache.json");

    private static readonly CachedToken Fresh = new("token", DateTimeOffset.UtcNow.AddMinutes(4));

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A store holding one profile for <c>https://site.example</c> / <c>cli-user</c>.</summary>
    private ConfigStore StoreWithProfile()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        store.Save(
            new CliConfig
            {
                Host = "https://site.example",
                ClientId = "cli-user",
                ClientSecret = "secret",
            }
        );
        return store;
    }

    /// <summary>Runs <c>logout</c> against <paramref name="store"/> with the file cache at <see cref="CachePath"/>.</summary>
    private int Logout(ConfigStore store)
    {
        var global = new GlobalOptions();
        var root = new RootCommand("test");
        global.AddTo(root);
        var auth = new UmbracoAuthService(
            new StubHttpClientFactory(),
            cache: new FileTokenCache(CachePath)
        );
        root.Add(LogoutCommand.Build(global, store, auth));

        var original = Console.Out;
        Console.SetOut(new StringWriter());
        try
        {
            return root.Parse("logout -o json").Invoke();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Logout_RemovesEveryCachedTokenForTheProfile()
    {
        var cache = new FileTokenCache(CachePath);
        cache.Write(
            UmbracoAuthService.CacheKey("https://site.example", "cli-user", "secret"),
            Fresh
        );
        cache.Write(
            UmbracoAuthService.CacheKey("https://site.example", "cli-user", "old-secret"),
            Fresh
        );

        Logout(StoreWithProfile());

        Assert.Null(
            cache.Read(
                UmbracoAuthService.CacheKey("https://site.example", "cli-user", "old-secret")
            )
        );
    }

    [Fact]
    public void Logout_KeepsOtherProfilesTokens()
    {
        var cache = new FileTokenCache(CachePath);
        var other = UmbracoAuthService.CacheKey("https://other.example", "cli-user", "secret");
        cache.Write(other, Fresh);

        Logout(StoreWithProfile());

        Assert.Equal(Fresh, cache.Read(other));
    }

    [Fact]
    public void Logout_NoCacheFile_StillSucceeds()
    {
        var exit = Logout(StoreWithProfile());

        Assert.Equal(0, exit);
    }
}
