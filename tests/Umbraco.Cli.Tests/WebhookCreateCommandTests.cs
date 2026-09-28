using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Webhooks;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>webhook create</c> with an unknown <c>--event</c> (#234, #278): the client reports which
/// aliases were unknown, and the command turns that into a "did you mean" hint and a pointer to
/// <c>webhook event list</c>. Runs the real command over the real client, so the whole path from
/// the refused alias to the error envelope is covered.
/// </summary>
[Collection("ConsoleCapture")]
public class WebhookCreateCommandTests
{
    /// <summary>A trimmed <c>GET webhook/events</c> body.</summary>
    private const string Events = """
        {
          "total": 2,
          "items": [
            { "eventName": "Content Published", "eventType": "Content", "alias": "Umbraco.ContentPublish" },
            { "eventName": "Media Saved", "eventType": "Media", "alias": "Umbraco.MediaSave" }
          ]
        }
        """;

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

        public bool Confirm(string message) => false;
    }

    /// <summary>Runs <c>webhook create</c> with <paramref name="events"/> and returns the error message.</summary>
    /// <param name="events">The <c>--event</c> value.</param>
    /// <returns>The <c>message</c> of the error envelope written to stderr.</returns>
    private static async Task<string> ErrorMessageFor(string events)
    {
        var client = Wire.Client(Wire.Routed(("/webhook/events", Events)));
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
        root.Add(WebhooksCommand.Build(new CommandExecutor(factory, new Prompt())));

        var err = new StringWriter();
        var (origOut, origErr) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(err);
        try
        {
            await root.Parse(
                    $"--host https://x --token t --output json webhook create --url https://my.app/hook --event {events}"
                )
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        using var doc = JsonDocument.Parse(err.ToString());
        return doc.RootElement.GetProperty("message").GetString()!;
    }

    [Fact]
    public async Task Create_UnknownEvent_SuggestsTheNearestAliasAndTheEventList()
    {
        var message = await ErrorMessageFor("ContentPublished");

        Assert.Equal(
            "Unknown webhook event 'ContentPublished' (did you mean 'Umbraco.ContentPublish'?). "
                + "Umbraco would save the webhook but never fire it. Run 'umbraco webhook event "
                + "list' for the valid aliases.",
            message
        );
    }

    [Fact]
    public async Task Create_EventInTheWrongCase_SuggestsTheCorrectSpelling()
    {
        var message = await ErrorMessageFor("umbraco.contentpublish");

        Assert.Contains("did you mean 'Umbraco.ContentPublish'", message);
    }

    [Fact]
    public async Task Create_UnrelatedEvent_NamesItWithoutASuggestion()
    {
        var message = await ErrorMessageFor("MemberGroupDeleted");

        Assert.StartsWith("Unknown webhook event 'MemberGroupDeleted'. Umbraco", message);
    }

    [Fact]
    public void WithEventSuggestions_FailureWithoutUnknownValues_IsReturnedUnchanged()
    {
        var failure = UmbracoResponse<WebhookResponse>.Failure(500, "Boom.");

        Assert.Same(failure, WebhooksCreateCommand.WithEventSuggestions(failure));
    }
}
