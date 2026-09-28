using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The <c>auth</c> commands that manage saved profiles, run end to end against a temp config:
/// logout names the profile it resolved (#301, #304), <c>auth profile list</c> emits real booleans
/// (#283), and the global output options apply under <c>auth</c> (#305).
/// </summary>
[Collection("ConsoleCapture")]
public sealed class AuthProfileCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"auth-{Guid.NewGuid():N}");

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("UMBRACO_PROFILE", null);
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A store with profiles <c>a</c> (the default) and <c>b</c>.</summary>
    private ConfigStore TwoProfiles()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        foreach (var name in new[] { "a", "b" })
            store.Save(
                new CliConfig
                {
                    Host = $"https://{name}.example",
                    ClientId = "id",
                    ClientSecret = "secret",
                },
                name
            );
        return store;
    }

    /// <summary>Runs an <c>auth</c> command line against <paramref name="store"/> and returns stdout's <c>data</c>.</summary>
    private JsonNode? Run(ConfigStore store, string args)
    {
        var global = new GlobalOptions();
        var root = new RootCommand("test");
        global.AddTo(root);
        var auth = new UmbracoAuthService(
            new StubHttpClientFactory(),
            cache: new FileTokenCache(Path.Combine(_dir, "token-cache.json"))
        );
        root.Add(LogoutCommand.Build(global, store, auth));
        var profile = new Command("profile");
        profile.Add(ProfilesCommand.Build(global, store));
        root.Add(profile);

        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            root.Parse(args).Invoke();
        }
        finally
        {
            Console.SetOut(original);
        }
        return JsonNode.Parse(captured.ToString())?["data"];
    }

    [Fact]
    public void Logout_UmbracoProfileEnv_ResponseNamesThatProfile()
    {
        var store = TwoProfiles();
        Environment.SetEnvironmentVariable("UMBRACO_PROFILE", "b");

        var data = Run(store, "logout -o json");

        Assert.Equal("b", data?["profile"]?.GetValue<string>());
    }

    [Fact]
    public void Logout_TheDefaultWithOthersLeft_ReportsTheDefaultCleared()
    {
        var store = TwoProfiles();

        var data = Run(store, "logout -o json");

        Assert.True(data?["defaultCleared"]?.GetValue<bool>());
    }

    [Fact]
    public void Logout_NotTheDefault_DoesNotReportTheDefaultCleared()
    {
        var store = TwoProfiles();

        var data = Run(store, "logout --profile b -o json");

        Assert.False(data?["defaultCleared"]?.GetValue<bool>());
    }

    [Fact]
    public void ProfileList_Json_MarksTheDefaultWithABoolean()
    {
        var store = TwoProfiles();

        var data = Run(store, "profile list -o json");

        Assert.Equal(
            """[{"profile":"a","default":true},{"profile":"b","default":false}]""",
            data?.ToJsonString()
        );
    }

    [Fact]
    public void ProfileList_Fields_KeepsOnlyTheNamedFields()
    {
        var store = TwoProfiles();

        var data = Run(store, "profile list -o json --fields profile");

        Assert.Equal("""[{"profile":"a"},{"profile":"b"}]""", data?.ToJsonString());
    }
}
