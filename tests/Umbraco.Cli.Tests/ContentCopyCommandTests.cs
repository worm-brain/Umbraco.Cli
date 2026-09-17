using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of <c>content copy</c> (#91): it threads the copy options through and
/// surfaces the new id as object output (so a script can chain to the copy). Client behaviour is
/// covered by <see cref="ContentCopyClientTests"/>.
/// </summary>
[Collection("ConsoleCapture")]
public class ContentCopyCommandTests
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

    private static (RootCommand Root, StringWriter Out) BuildRoot(IUmbracoManagementClient client)
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
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(ContentCommand.Build(executor));
        return (root, new StringWriter());
    }

    private static async Task<(int Exit, string Output)> Run(
        RootCommand root,
        StringWriter sw,
        string args
    )
    {
        var orig = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exit = await root.Parse(args).InvokeAsync();
            return (exit, sw.ToString());
        }
        finally
        {
            Console.SetOut(orig);
        }
    }

    private const string Auth = "--host https://x --token t --output json";

    [Fact]
    public async Task Copy_ThreadsOptionsAndSurfacesNewId()
    {
        var newId = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient
        {
            CopyContentResponse = UmbracoResponse<ContentItemResponse>.Success(
                new ContentItemResponse { Id = newId, Name = "About (copy)" }
            ),
        };
        var (root, sw) = BuildRoot(fake);
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();

        var (exit, output) = await Run(root, sw, $"{Auth} content copy {id} --parent {parent}");

        Assert.Equal(0, exit);
        Assert.Equal((id, parent), fake.LastCopyArgs);
        // The new id reaches the JSON output, so an agent can chain to the copy.
        Assert.Contains(newId.ToString(), output);
    }
}
