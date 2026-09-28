using System.CommandLine;
using System.Net;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Media;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>media update</c> (#220): a custom media type's fields can be set after upload, merged into
/// the item so the uploaded file and every value not named survive.
/// </summary>
[Collection("ConsoleCapture")]
public class MediaUpdateTests
{
    private static readonly Guid Id = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    // ── client: the merge on the wire ─────────────────────────────────────────

    private static readonly string Media = $$"""
        { "id": "{{Id}}", "mediaType": { "id": "44444444-4444-4444-4444-444444444444" },
          "values": [
            { "alias": "umbracoFile", "culture": null, "segment": null, "value": { "src": "/media/a.pdf" } },
            { "alias": "title", "culture": null, "segment": null, "value": "Old" } ],
          "variants": [ { "culture": null, "segment": null, "name": "Forms" } ] }
        """;

    /// <summary>A handler whose media GET returns <see cref="Media"/> and accepts the PUT.</summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler Handler() =>
        new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, Media)
            .When(_ => true, HttpStatusCode.OK, "");

    [Fact]
    public async Task UpdateMediaAsync_NewValue_KeepsTheUploadedFile()
    {
        var handler = Handler();

        var result = await Wire.Client(handler)
            .UpdateMediaAsync(
                Id,
                new UpdateMediaRequest
                {
                    Values = [new ContentValue { Alias = "summary", Value = "S" }],
                },
                ct: CancellationToken.None
            );

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var aliases = handler.BodyOf(HttpMethod.Put, $"/media/{Id}")["values"]!
            .AsArray()
            .Select(v => v!["alias"]!.GetValue<string>())
            .ToList();
        Assert.Equal(["umbracoFile", "title", "summary"], aliases);
    }

    [Fact]
    public async Task UpdateMediaAsync_ExistingValue_IsReplacedInPlace()
    {
        var handler = Handler();

        await Wire.Client(handler)
            .UpdateMediaAsync(
                Id,
                new UpdateMediaRequest
                {
                    Values = [new ContentValue { Alias = "title", Value = "New" }],
                },
                ct: CancellationToken.None
            );

        var title = handler.BodyOf(HttpMethod.Put, $"/media/{Id}")["values"]!
            .AsArray()
            .Single(v => v!["alias"]!.GetValue<string>() == "title")!;
        Assert.Equal("New", title["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateMediaAsync_ReadFails_WritesNothing()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.NotFound, "");

        var result = await Wire.Client(handler)
            .UpdateMediaAsync(Id, new UpdateMediaRequest(), ct: CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        handler.AssertNoRequest(HttpMethod.Put, $"/media/{Id}");
    }

    // ── command: flags onto the request ───────────────────────────────────────

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

    /// <summary>Runs a <c>media</c> command line against the fake.</summary>
    /// <param name="fake">The fake client.</param>
    /// <param name="args">The arguments after <c>media</c>; <c>{body}</c> becomes a temp file.</param>
    /// <param name="body">The JSON for <c>{body}</c>.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> Run(
        FakeUmbracoManagementClient fake,
        string args,
        string body = "{}"
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
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(MediaCommand.Build(new CommandExecutor(factory, new Prompt())));

        var path = Path.Combine(Path.GetTempPath(), $"body-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, body);
        var (out_, err) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        try
        {
            return await root.Parse(
                    $"--host https://x --token t --output json media {args.Replace("{body}", path)}"
                )
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Update_Values_SendsEachAsAValue()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, $"update {Id} --value summary=S --value pageCount=4");

        Assert.Equal(0, exit);
        Assert.Equal(
            ["summary=S", "pageCount=4"],
            fake.LastMediaUpdate!.Value.Request.Values.Select(v => $"{v.Alias}={v.Value}")
        );
    }

    [Fact]
    public async Task Update_EmptyValue_SendsAnEmptyValueToClearTheProperty()
    {
        var fake = new FakeUmbracoManagementClient();

        await Run(fake, $"update {Id} --value summary=");

        // docs/conventions.md 4.3: key= clears a value that may be empty.
        Assert.Equal(
            ["summary="],
            fake.LastMediaUpdate!.Value.Request.Values.Select(v => $"{v.Alias}={v.Value}")
        );
    }

    [Fact]
    public async Task Update_Name_SendsASingleVariant()
    {
        var fake = new FakeUmbracoManagementClient();

        await Run(fake, $"update {Id} --name Brochure");

        Assert.Equal("Brochure", Assert.Single(fake.LastMediaUpdate!.Value.Request.Variants).Name);
    }

    [Fact]
    public async Task Update_ValueFlagAndBody_TheFlagWins()
    {
        var fake = new FakeUmbracoManagementClient();

        await Run(
            fake,
            $"update {Id} --json-body {{body}} --value title=Flag",
            """{ "values": [ { "alias": "title", "value": "Body" }, { "alias": "summary", "value": "S" } ] }"""
        );

        Assert.Equal(
            ["title=Flag", "summary=S"],
            fake.LastMediaUpdate!.Value.Request.Values.Select(v => $"{v.Alias}={v.Value}")
        );
    }

    [Fact]
    public async Task Update_ValueFlag_LeavesTheBodysCultureSpecificValueAlone()
    {
        var fake = new FakeUmbracoManagementClient();

        await Run(
            fake,
            $"update {Id} --json-body {{body}} --value title=Flag",
            """{ "values": [ { "alias": "title", "culture": "da-DK", "value": "Dansk" } ] }"""
        );

        // Keyed like the merge (alias + culture + segment): the invariant flag is a new entry.
        Assert.Equal(
            ["title/da-DK=Dansk", "title/=Flag"],
            fake.LastMediaUpdate!.Value.Request.Values.Select(v =>
                $"{v.Alias}/{v.Culture}={v.Value}"
            )
        );
    }

    [Theory]
    [InlineData("update 3f7a8b2e-1234-5678-abcd-ef0123456789")]
    [InlineData("update 3f7a8b2e-1234-5678-abcd-ef0123456789 --value umbracoFile=x")]
    [InlineData("update 3f7a8b2e-1234-5678-abcd-ef0123456789 --name N --replace")]
    public async Task Update_InvalidInput_IsRefusedBeforeTheClient(string args)
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, args);

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastMediaUpdate);
    }
}
