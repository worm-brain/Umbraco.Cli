using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.ContentTypes;
using Umbraco.Cli.Commands.DataTypes;
using Umbraco.Cli.Commands.MediaTypes;
using Umbraco.Cli.Commands.MemberTypes;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Type deletes refuse to take content with them without --force (#246, #253). Umbraco cascades
/// these deletes - a data type takes its properties and their values, a member type its members, a
/// document or media type every item of that type - so the CLI checks first, as the backoffice does.
/// </summary>
[Collection("ConsoleCapture")]
public class TypeDeleteGuardTests
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

    private sealed class NonInteractive : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => false;
    }

    /// <summary>An interactive prompt that says yes, and records whether it was asked.</summary>
    private sealed class RecordingYes : IConfirmationPrompt
    {
        public bool Asked { get; private set; }

        public bool IsInteractive => true;

        public bool Confirm(string message) => Asked = true;
    }

    /// <summary>Runs a command line against <paramref name="fake"/>, returning its exit code and stderr.</summary>
    private static async Task<(int Exit, string Stderr)> Run(
        FakeUmbracoManagementClient fake,
        string args,
        IConfirmationPrompt? prompt = null
    )
    {
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")),
            new UmbracoAuthService(stub),
            stub,
            global,
            new FakeClientFactory(fake),
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, prompt ?? new NonInteractive());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(DataTypesCommand.Build(executor));
        root.Add(ContentTypesCommand.Build(executor));
        root.Add(MediaTypesCommand.Build(executor));
        root.Add(MemberTypesCommand.Build(executor));

        var (outWriter, errWriter) = (new StringWriter(), new StringWriter());
        var (origOut, origErr) = (Console.Out, Console.Error);
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exit = await root.Parse($"--host https://x --token t --output json {args}")
                .InvokeAsync();
            return (exit, errWriter.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private static readonly Guid TypeId = Guid.Parse("531e54e3-0000-0000-0000-000000000001");

    // ── data types (#246) ─────────────────────────────────────────────────────

    [Fact]
    public async Task DataTypesDelete_InUse_IsRefusedAndNothingIsDeleted()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.UsedDataTypes[TypeId] = [("Blog Post", "Categories")];

        var (exit, _) = await Run(fake, $"data-type delete {TypeId} --yes");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task DataTypesDelete_InUse_NamesWhatUsesItAndTheOverride()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.UsedDataTypes[TypeId] = [("Blog Post", "Categories")];

        var (_, stderr) = await Run(fake, $"data-type delete {TypeId} --yes");

        // Read the envelope's message: the JSON writer escapes '>' on the wire.
        var message = System
            .Text.Json.JsonDocument.Parse(stderr)
            .RootElement.GetProperty("message")
            .GetString();
        Assert.Contains("Blog Post > Categories", message);
        Assert.Contains("--force", message);
    }

    [Fact]
    public async Task DataTypesDelete_InUse_IsRefusedBeforeTheUserIsAskedToConfirm()
    {
        // Review finding: the check ran after the prompt, so a user confirmed a delete that was
        // then refused.
        var fake = new FakeUmbracoManagementClient();
        fake.UsedDataTypes[TypeId] = [("Blog Post", "Categories")];
        var prompt = new RecordingYes();

        var (exit, _) = await Run(fake, $"data-type delete {TypeId}", prompt);

        Assert.Equal(2, exit);
        Assert.False(prompt.Asked);
    }

    [Fact]
    public async Task DataTypesDelete_NotInUse_AsksToConfirmAndDeletes()
    {
        var fake = new FakeUmbracoManagementClient();
        var prompt = new RecordingYes();

        var (exit, _) = await Run(fake, $"data-type delete {TypeId}", prompt);

        Assert.Equal(0, exit);
        Assert.True(prompt.Asked);
    }

    [Fact]
    public async Task DataTypesDelete_InUseUnderDryRun_IsRefusedAsARealRunWouldBe()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.UsedDataTypes[TypeId] = [("Blog Post", "Categories")];

        var (exit, _) = await Run(fake, $"--dry-run data-type delete {TypeId}");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task DataTypesDelete_InUseWithForce_Deletes()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.UsedDataTypes[TypeId] = [("Blog Post", "Categories")];

        var (exit, _) = await Run(fake, $"data-type delete {TypeId} --force --yes");

        Assert.Equal(0, exit);
        Assert.Equal([TypeId], fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task DataTypesDelete_NotInUse_DeletesWithJustYes()
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, $"data-type delete {TypeId} --yes");

        Assert.Equal(0, exit);
        Assert.Equal([TypeId], fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task DataTypesDelete_ForceWithoutYes_IsStillRefusedNonInteractively()
    {
        // --force overrides the in-use check, not the confirmation.
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, $"data-type delete {TypeId} --force");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SchemaDeletedIds);
    }

    // ── document and media types (#253): Umbraco cannot count their items ─────

    [Theory]
    [InlineData("document-type")]
    [InlineData("media-type")]
    public async Task TypeDelete_WithoutForce_IsRefused(string noun)
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, $"{noun} delete {TypeId} --yes");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SchemaDeletedIds);
    }

    [Theory]
    [InlineData("document-type")]
    [InlineData("media-type")]
    public async Task TypeDelete_WithForce_Deletes(string noun)
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, $"{noun} delete {TypeId} --force --yes");

        Assert.Equal(0, exit);
        Assert.Equal([TypeId], fake.SchemaDeletedIds);
    }

    // ── member types (#253) ───────────────────────────────────────────────────

    [Fact]
    public async Task MemberTypesDelete_WithMembers_IsRefusedWithTheCount()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.MemberCountsByType[TypeId] = 3;

        var (exit, stderr) = await Run(fake, $"member-type delete {TypeId} --yes");

        Assert.Equal(2, exit);
        Assert.Contains("3 member(s)", stderr);
    }

    [Fact]
    public async Task MemberTypesDelete_NoMembers_DeletesWithJustYes()
    {
        var fake = new FakeUmbracoManagementClient();

        var (exit, _) = await Run(fake, $"member-type delete {TypeId} --yes");

        Assert.Equal(0, exit);
        Assert.Equal([TypeId], fake.SchemaDeletedIds);
    }
}
