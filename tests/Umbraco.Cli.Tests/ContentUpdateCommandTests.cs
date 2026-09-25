using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of <c>content update</c> (#178/#179/#162): merging is the default, the
/// destructive path is opt-in, and <c>--template</c> is read as either an alias or a UUID. The
/// merge itself lives in the client and is covered by <see cref="ContentUpdateMergeClientTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class ContentUpdateCommandTests
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

    private sealed class Prompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => true;
    }

    /// <summary>
    /// Builds a root command wired to <paramref name="client"/>, with a host and token supplied so
    /// the context factory does not abort before the command runs.
    /// </summary>
    /// <param name="client">The fake client to route calls to.</param>
    /// <returns>The root command.</returns>
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
        var executor = new CommandExecutor(factory, new Prompt());
        var root = new RootCommand("test");
        global.AddTo(root);
        root.Add(ContentCommand.Build(executor));
        return root;
    }

    /// <summary>Runs a command line against the fake, writing the body to a temp file first.</summary>
    /// <param name="fake">The fake client.</param>
    /// <param name="args">The command line, with <c>{body}</c> replaced by the temp file path.</param>
    /// <param name="body">The JSON body to write.</param>
    /// <returns>The process exit code.</returns>
    private static async Task<int> RunAsync(
        FakeUmbracoManagementClient fake,
        string args,
        string body
    )
    {
        var path = Path.Combine(Path.GetTempPath(), $"body-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, body);
        try
        {
            var line = args.Replace("{body}", path) + " --host https://example.com --token t";
            return await BuildRoot(fake).Parse(line).InvokeAsync();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static readonly Guid Id = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    // content create shares this harness: --culture (#228) is a flag on the same noun.

    [Fact]
    public async Task Create_WithCulture_PutsItOnTheVariant()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            "content create --content-type blogPost --name Post --culture da-DK",
            "{}"
        );

        Assert.Equal(0, exit);
        Assert.Equal("da-DK", Assert.Single(fake.LastCreate!.Variants).Culture);
    }

    [Fact]
    public async Task Create_WithoutCulture_LeavesItForTheClientToDefault()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(fake, "content create --content-type blogPost --name Post", "{}");

        Assert.Null(Assert.Single(fake.LastCreate!.Variants).Culture);
    }

    [Fact]
    public async Task Update_WithoutReplace_AsksTheClientToMerge()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}}",
            """{"values":[]}"""
        );

        Assert.Equal(0, exit);
        Assert.False(fake.LastUpdate!.Value.Replace);
    }

    [Fact]
    public async Task Update_WithReplace_AsksTheClientToReplace()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}} --replace",
            """{"values":[]}"""
        );

        Assert.Equal(0, exit);
        Assert.True(fake.LastUpdate!.Value.Replace);
    }

    [Fact]
    public async Task Update_TemplateAlias_IsPassedAsAnAlias()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}} --template blogPost",
            """{"values":[]}"""
        );

        Assert.Equal("blogPost", fake.LastUpdate!.Value.Request.Template!.Alias);
        Assert.Null(fake.LastUpdate!.Value.Request.Template!.Id);
    }

    [Fact]
    public async Task Update_TemplateWithoutBody_MergesAnEmptyRequestWithTheTemplate()
    {
        // #208: a template-only change used to need `echo '{}' | ... --json-body -`.
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(fake, $"content update {Id} --template blogPost", "");

        Assert.Equal(0, exit);
        var update = fake.LastUpdate!.Value;
        Assert.Equal("blogPost", update.Request.Template!.Alias);
        Assert.Empty(update.Request.Values);
        Assert.False(update.Replace);
    }

    [Fact]
    public async Task Update_ReplaceWithoutBody_IsRefusedBeforeAnyRequest()
    {
        // A replace with an empty body would clear every value on the item.
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(fake, $"content update {Id} --template blogPost --replace", "");

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastUpdate);
    }

    [Fact]
    public async Task Update_NeitherBodyNorTemplate_IsRefused()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(fake, $"content update {Id}", "");

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastUpdate);
    }

    [Fact]
    public async Task Update_TemplateUuid_IsPassedAsAnId()
    {
        var fake = new FakeUmbracoManagementClient();
        var templateId = Guid.NewGuid();

        await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}} --template {templateId}",
            """{"values":[]}"""
        );

        Assert.Equal(templateId, fake.LastUpdate!.Value.Request.Template!.Id);
    }

    [Fact]
    public async Task Update_NoTemplateOption_LeavesTemplateUnset()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(fake, $"content update {Id} --json-body {{body}}", """{"values":[]}""");

        Assert.Null(fake.LastUpdate!.Value.Request.Template);
    }
}
