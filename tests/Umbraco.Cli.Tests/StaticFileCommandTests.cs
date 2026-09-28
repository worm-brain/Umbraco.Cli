using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.StaticFiles;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// End-to-end command-layer behaviour of the static-file nouns (#105): the branches that live in
/// the command rather than the client - the <c>update</c> "content required" guard and the
/// <c>--content-file</c>-over-<c>--content</c> precedence.
/// </summary>
[Collection("ConsoleCapture")]
public class StaticFileCommandTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    private sealed class Prompt(bool interactive, bool answer) : IConfirmationPrompt
    {
        public bool IsInteractive => interactive;

        public bool Confirm(string message) => answer;
    }

    private static RootCommand BuildRoot(IUmbracoManagementClient client)
    {
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")),
            new UmbracoAuthService(stub),
            stub,
            global,
            new FakeClientFactory(client),
            new MutationInterceptState()
        );
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(
            StaticFileCommand.Build(
                new CommandExecutor(factory, new Prompt(interactive: false, answer: true)),
                StaticFileKind.Script,
                "script",
                "script"
            )
        );
        return root;
    }

    private static async Task<int> Run(RootCommand root, string args)
    {
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            return await root.Parse(args).InvokeAsync();
        }
        finally
        {
            Console.SetOut(orig);
        }
    }

    private const string Auth = "--host https://x --token t --output json";

    [Fact]
    public async Task Update_WithoutContent_FailsAndSendsNoWrite()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} script update site.js");

        Assert.Equal(1, exit); // the command's "content required" guard maps to an API-style failure
        Assert.Empty(fake.StaticFilesUpdated);
    }

    [Fact]
    public async Task Update_WithContent_SendsThatContent()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} script update site.js --content \"// hi\"");

        Assert.Equal(0, exit);
        var update = Assert.Single(fake.StaticFilesUpdated);
        Assert.Equal("site.js", update.Path);
        Assert.Equal("// hi", update.Content);
    }

    [Fact]
    public async Task ContentAndContentFile_Together_AreRefusedAndNothingIsCreated()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sf-{Guid.NewGuid()}.js");
        await File.WriteAllTextAsync(file, "// from file");
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        try
        {
            var exit = await Run(
                root,
                $"{Auth} script create --name site.js --content \"// inline\" --content-file {file}"
            );

            // Two sources for one value is an input error, not a silent pick (docs/conventions.md 4.5).
            Assert.Equal(1, exit);
            Assert.Empty(fake.StaticFilesCreated);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Create_WithoutContent_DefaultsToEmpty()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake);

        var exit = await Run(root, $"{Auth} script create --name site.js");

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.StaticFilesCreated);
        Assert.Equal("", created.Request.Content);
    }

    [Fact]
    public async Task FolderCreate_PassesNameAndParent()
    {
        // #238: a folder a fresh site lacks can now be made from the CLI.
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(
            BuildRoot(fake),
            $"{Auth} script folder create --name Components --parent blocklist"
        );

        Assert.Equal(
            (0, (StaticFileKind.Script, "Components", (string?)"blocklist")),
            (exit, Assert.Single(fake.StaticFileFoldersCreated))
        );
    }

    [Fact]
    public async Task FolderDelete_WithYes_DeletesThePath()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(BuildRoot(fake), $"{Auth} script folder delete lib --yes");

        Assert.Equal(
            (0, (StaticFileKind.Script, "lib")),
            (exit, Assert.Single(fake.StaticFileFoldersDeleted))
        );
    }

    [Fact]
    public async Task FolderDelete_WithoutYes_IsRefusedAndDeletesNothing()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(BuildRoot(fake), $"{Auth} script folder delete lib");

        Assert.Equal((2, 0), (exit, fake.StaticFileFoldersDeleted.Count));
    }
}
