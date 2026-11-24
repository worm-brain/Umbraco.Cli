using System.CommandLine;
using Umbraco.Community.Cli.Client;
using Umbraco.Community.Cli.Commands;
using Umbraco.Community.Cli.Commands.Content;
using Umbraco.Community.Cli.Infrastructure;
using Umbraco.Community.Cli.Infrastructure.Config;
using Umbraco.Community.Cli.Infrastructure.Http;

namespace Umbraco.Community.Cli.Tests;

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

    // content create's body errors share this harness too (#459).

    [Fact]
    public async Task Create_JsonBodyWithAStringContentType_ExplainsTheShapeWithoutTypeNames()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var (out_, err) = (Console.Out, Console.Error);
        var stderr = new StringWriter();
        Console.SetOut(new StringWriter());
        Console.SetError(stderr);

        // Act
        int exit;
        try
        {
            exit = await RunAsync(
                fake,
                "content create --json-body {body} --output json",
                """{"contentType":"probe","variants":[{"name":"P1"}]}"""
            );
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }

        // Assert
        var message = System.Text.Json.Nodes.JsonNode.Parse(stderr.ToString())![
            "message"
        ]!.GetValue<string>();
        Assert.Equal(
            (1, true, true, false),
            (
                exit,
                message.Contains("contentType must be", StringComparison.Ordinal),
                message.Contains("""{"alias": "blogPost"}""", StringComparison.Ordinal),
                message.Contains("Umbraco.", StringComparison.Ordinal)
            )
        );
    }

    // ── unknown keys in the typed bodies (#474) ───────────────────────────────

    /// <summary>Runs a command line with stdout and stderr captured.</summary>
    /// <param name="fake">The fake client.</param>
    /// <param name="args">The command line, with <c>{body}</c> replaced by the temp file path.</param>
    /// <param name="body">The JSON body to write.</param>
    /// <returns>The exit code, stdout and stderr.</returns>
    private static async Task<(int Exit, string Out, string Err)> RunCapturingAsync(
        FakeUmbracoManagementClient fake,
        string args,
        string body
    )
    {
        var (out_, err) = (Console.Out, Console.Error);
        var (stdout, stderr) = (new StringWriter(), new StringWriter());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunAsync(fake, args + " --output json", body);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }
    }

    [Fact]
    public async Task Update_JsonBodyWithUnknownKeys_FailsWithTheirPathsAndSendsNothing()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();

        // Act
        var (exit, _, err) = await RunCapturingAsync(
            fake,
            $"content update {Id} --json-body {{body}}",
            """{"valuez":[{"alias":"title","value":"x"}],"variants":[{"culture":null,"nmae":"x"}]}"""
        );

        // Assert
        var error = System.Text.Json.Nodes.JsonNode.Parse(err)!;
        Assert.Equal(
            (1, "invalid_argument", true, true),
            (
                exit,
                error["category"]!.GetValue<string>(),
                error["message"]!
                    .GetValue<string>()
                    .Contains("$.valuez, $.variants[0].nmae", StringComparison.Ordinal),
                fake.LastUpdate is null
            )
        );
    }

    [Fact]
    public async Task Create_JsonBodyWithAnUnknownKey_FailsAndSendsNothing()
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _, err) = await RunCapturingAsync(
            fake,
            "content create --json-body {body}",
            """{"contentType":{"alias":"blogPost"},"variants":[{"name":"P"}],"parnet":null}"""
        );

        Assert.Equal(
            (1, true, true),
            (exit, err.Contains("$.parnet", StringComparison.Ordinal), fake.LastCreate is null)
        );
    }

    [Fact]
    public async Task Update_ContentGetOutputWrittenBack_IsApplied()
    {
        // Arrange: a document with every key content get prints, read through content get itself.
        var fake = new FakeUmbracoManagementClient();
        fake.ContentById[Id] = new ContentItemResponse
        {
            Id = Id,
            Name = "Home",
            DocumentType = new ContentTypeRef
            {
                Id = Guid.NewGuid(),
                Alias = "home",
                Icon = "icon-home",
                Collection = new ContentParentReference { Id = Guid.NewGuid() },
            },
            Parent = new ContentParentReference { Id = Guid.NewGuid() },
            Urls = [new UrlInfo { Culture = "en-US", Url = "/" }],
            IsPublished = true,
            Flags = [new FlagResponse { Alias = "Umb.ScheduledForPublish" }],
            UpdateDate = DateTimeOffset.UnixEpoch,
            Values =
            [
                new ContentValueResponse
                {
                    Alias = "title",
                    Culture = "en-US",
                    EditorAlias = "Umbraco.TextBox",
                    Value = "Welcome",
                },
            ],
            Variants =
            [
                new ContentVariantResponse
                {
                    Culture = "en-US",
                    Name = "Home",
                    State = "Published",
                    CreateDate = DateTimeOffset.UnixEpoch,
                    UpdateDate = DateTimeOffset.UnixEpoch,
                    PublishDate = DateTimeOffset.UnixEpoch,
                    ScheduledPublishDate = DateTimeOffset.UnixEpoch,
                    ScheduledUnpublishDate = DateTimeOffset.UnixEpoch,
                    Id = Guid.NewGuid(),
                    Flags = [new FlagResponse { Alias = "Umb.ScheduledForPublish" }],
                },
            ],
            Template = new ContentTemplateReference { Id = Guid.NewGuid(), Alias = "home" },
        };
        var (_, got, _) = await RunCapturingAsync(fake, $"content get {Id}", "{}");
        var edited = System.Text.Json.Nodes.JsonNode.Parse(got)!["data"]!;
        edited["variants"]![0]!["name"] = "Start";

        // Act
        var exit = await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}}",
            edited.ToJsonString()
        );

        // Assert
        Assert.Equal((0, "Start"), (exit, fake.LastUpdate!.Value.Request.Variants.Single().Name));
    }

    [Fact]
    public async Task Update_AllowUnknownKeys_AppliesTheKnownKeysAndDropsTheRest()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}} --allow-unknown-keys",
            """{"valuez":[],"variants":[{"culture":null,"name":"Kept"}]}"""
        );

        Assert.Equal((0, "Kept"), (exit, fake.LastUpdate!.Value.Request.Variants.Single().Name));
    }

    [Fact]
    public async Task Update_AllowUnknownKeysWithoutABody_IsAParseError()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            $"content update {Id} --template home --allow-unknown-keys",
            "{}"
        );

        Assert.Equal((false, true), (exit == 0, fake.LastUpdate is null));
    }

    // content restore shares this harness too (#230).

    [Fact]
    public async Task Restore_ToRoot_AsksTheClientForTheRoot()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(fake, $"content restore {Id} --to-root", "{}");

        Assert.Equal((Id, RestoreTarget.Root), fake.LastRestore);
    }

    [Fact]
    public async Task Restore_ParentAndToRoot_IsRefusedBeforeTheClientIsCalled()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            $"content restore {Id} --parent {Guid.NewGuid()} --to-root",
            "{}"
        );

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastRestore);
    }

    // --publish on restore and rollback (#233): both writes leave the live site as it was.

    [Fact]
    public async Task Restore_WithPublish_PublishesTheRestoredItemInEveryCulture()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };

        await RunAsync(fake, $"content restore {Id} --publish", "{}");

        Assert.Equal(("publish", Id, (List<string>?)null), Assert.Single(fake.StateCalls));
    }

    [Fact]
    public async Task Restore_WithoutPublish_LeavesTheItemUnpublished()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(fake, $"content restore {Id}", "{}");

        Assert.Empty(fake.StateCalls);
    }

    [Fact]
    public async Task Restore_WithPublish_PublishFailure_ExitsNonZero()
    {
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Failure(400, "Missing title"),
        };

        var exit = await RunAsync(fake, $"content restore {Id} --publish", "{}");

        Assert.NotEqual(0, exit);
    }

    private static readonly Guid VersionId = Guid.Parse("7a1c2d3e-1234-5678-abcd-ef0123456789");

    [Fact]
    public async Task Rollback_WithPublish_PublishesTheVersionsDocumentInThatCulture()
    {
        var fake = new FakeUmbracoManagementClient
        {
            VersionDocumentId = Id,
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
        };

        await RunAsync(
            fake,
            $"content version rollback {VersionId} --culture en-US --publish",
            "{}"
        );

        var call = Assert.Single(fake.StateCalls);
        Assert.Equal(
            ("publish", Id, "en-US"),
            (call.Operation, call.Id, Assert.Single(call.Cultures!))
        );
    }

    [Fact]
    public async Task Rollback_WithPublish_UnknownVersion_RollsNothingBack()
    {
        // VersionDocumentId unset: the version read 404s, before anything is written.
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(fake, $"content version rollback {VersionId} --publish", "{}");

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastRollback);
    }

    [Fact]
    public async Task Rollback_WithoutPublish_OnlyRollsBack()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(fake, $"content version rollback {VersionId} --culture en-US", "{}");

        Assert.Equal((VersionId, "en-US"), fake.LastRollback);
        Assert.Empty(fake.StateCalls);
    }

    // content create shares this harness: --culture (#228) is a flag on the same noun.

    [Fact]
    public async Task Create_WithCulture_PutsItOnTheVariant()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            "content create --document-type blogPost --name Post --culture da-DK",
            "{}"
        );

        Assert.Equal(0, exit);
        Assert.Equal("da-DK", Assert.Single(fake.LastCreate!.Variants).Culture);
    }

    [Fact]
    public async Task Create_WithoutCulture_LeavesItForTheClientToDefault()
    {
        var fake = new FakeUmbracoManagementClient();

        await RunAsync(fake, "content create --document-type blogPost --name Post", "{}");

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
        Assert.Equal(WriteMode.Merge, fake.LastUpdate!.Value.Mode);
    }

    [Fact]
    public async Task Update_WithReplace_AsksTheClientToReplace()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await RunAsync(
            fake,
            $"content update {Id} --json-body {{body}} --replace --yes",
            """{"values":[]}"""
        );

        Assert.Equal(0, exit);
        Assert.Equal(WriteMode.Replace, fake.LastUpdate!.Value.Mode);
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
        Assert.Equal(WriteMode.Merge, update.Mode);
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
