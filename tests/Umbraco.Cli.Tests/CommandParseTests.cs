using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Auth;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Commands.Languages;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Commands.Users;
using Umbraco.Cli.Commands.Webhooks;
using Umbraco.Cli.Infrastructure.Config;

namespace Umbraco.Cli.Tests;

public class CommandParseTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static RootCommand BuildRoot()
    {
        var stub = new StubHttpClientFactory();
        var configStore = new ConfigStore(
            Path.Combine(Path.GetTempPath(), $"umbraco-parse-test-{Guid.NewGuid()}.json")
        );
        var authService = new UmbracoAuthService(stub);
        var globalOptions = new GlobalOptions();
        var factory = new CommandContextFactory(
            configStore,
            authService,
            stub,
            globalOptions,
            new UmbracoManagementClientFactory()
        );
        var executor = new CommandExecutor(factory);

        var root = new RootCommand("Umbraco CLI");
        globalOptions.AddTo(root);

        root.Add(AuthCommand.Build(globalOptions, configStore, authService, executor));
        root.Add(ContentCommand.Build(executor));
        root.Add(MediaCommand.Build(executor));
        root.Add(ContentTypesCommand.Build(executor));
        root.Add(DataTypesCommand.Build(executor));
        root.Add(LanguagesCommand.Build(executor));
        root.Add(TemplatesCommand.Build(executor));
        root.Add(MembersCommand.Build(executor));
        root.Add(UsersCommand.Build(executor));
        root.Add(DictionaryCommand.Build(executor));
        root.Add(WebhooksCommand.Build(executor));

        return root;
    }

    private static bool HasErrors(string args)
    {
        var parsed = BuildRoot().Parse(args);
        return parsed.Errors.Count > 0;
    }

    // ── Command tree structure ────────────────────────────────────────────────

    [Theory]
    [InlineData("auth")]
    [InlineData("content")]
    [InlineData("media")]
    [InlineData("content-types")]
    [InlineData("data-types")]
    [InlineData("languages")]
    [InlineData("templates")]
    [InlineData("members")]
    [InlineData("users")]
    [InlineData("dictionary")]
    [InlineData("webhooks")]
    public void RootCommand_ContainsExpectedSubcommand(string subcommand)
    {
        var root = BuildRoot();
        var names = root.Subcommands.Select(c => c.Name).ToList();
        Assert.Contains(subcommand, names);
    }

    [Theory]
    [InlineData(
        "content",
        new[] { "list", "get", "create", "update", "delete", "publish", "unpublish" }
    )]
    [InlineData("media", new[] { "list", "get", "upload", "delete" })]
    [InlineData("auth", new[] { "login", "logout", "whoami" })]
    public void SubcommandGroup_ContainsExpectedVerbs(string group, string[] verbs)
    {
        var root = BuildRoot();
        var groupCmd = root.Subcommands.First(c => c.Name == group);
        var subNames = groupCmd.Subcommands.Select(c => c.Name).ToList();

        foreach (var verb in verbs)
            Assert.Contains(verb, subNames);
    }

    // ── Valid parses → no errors ──────────────────────────────────────────────

    [Theory]
    [InlineData("content list")]
    [InlineData("content list --skip 0 --take 50")]
    [InlineData("content list --take 100")]
    [InlineData("content list --parent 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content delete 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content publish 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content unpublish 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("media list")]
    [InlineData("media get 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("content-types list")]
    [InlineData("data-types list")]
    [InlineData("languages list")]
    [InlineData("templates list")]
    [InlineData("members list")]
    [InlineData("users list")]
    [InlineData("dictionary list")]
    [InlineData("webhooks list")]
    [InlineData("auth login --host https://example.com --client-id foo --client-secret bar")]
    [InlineData("auth logout")]
    [InlineData("auth whoami")]
    public void ValidArgs_ProduceNoParseErrors(string args)
    {
        Assert.False(HasErrors(args), $"Unexpected parse errors for: {args}");
    }

    // ── Global options on valid commands ──────────────────────────────────────

    [Theory]
    [InlineData("--output json content list")]
    [InlineData("--host https://x.com content list")]
    [InlineData("-H https://x.com content list")]
    [InlineData("-o json content list")]
    [InlineData("--verbose content list")]
    [InlineData("-v content list")]
    [InlineData("--token bearer123 content list")]
    [InlineData("content list --skip 5 --take 10")]
    public void GlobalOptions_ValidCombinations_NoParseErrors(string args)
    {
        Assert.False(HasErrors(args), $"Unexpected parse errors for: {args}");
    }

    // ── Invalid parses → produce errors ──────────────────────────────────────

    [Theory]
    [InlineData("content list --take abc")]
    [InlineData("content list --skip -1.5")]
    [InlineData("content get not-a-uuid")]
    [InlineData("content delete not-a-uuid")]
    [InlineData("totally-unknown-command")]
    [InlineData("content unknown-verb")]
    [InlineData("auth unknown-verb")]
    public void InvalidArgs_ProduceParseErrors(string args)
    {
        Assert.True(HasErrors(args), $"Expected parse errors for: {args}");
    }
}
