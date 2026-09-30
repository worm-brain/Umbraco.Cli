using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What an empty value (<c>key=</c>) means on a <c>key=value</c> option (docs/conventions.md 4.3,
/// #394): it clears a value that may be empty and removes an entry that cannot exist without one.
/// </summary>
[Collection("ConsoleCapture")]
public class EmptyPairValueTests
{
    private static readonly Guid Id = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    /// <summary>Runs a command line against the fake and returns its exit code.</summary>
    /// <param name="client">The fake client the command talks to.</param>
    /// <param name="args">The command line after the global options.</param>
    /// <returns>The exit code.</returns>
    private static async Task<int> Run(IUmbracoManagementClient client, string args)
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
        var executor = new CommandExecutor(factory, new ConsoleConfirmationPrompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(ContentCommand.Build(executor));
        root.Add(MembersCommand.Build(executor));

        var (out_, err) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        try
        {
            return await root.Parse($"--host https://x --token t --output json {args}")
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }
    }

    // -- property values: key= clears the value ------------------------------------

    [Fact]
    public async Task MemberUpdate_EmptyValue_SendsAnEmptyValueToClearTheProperty()
    {
        var fake = new FakeUmbracoManagementClient();

        await Run(fake, $"member update {Id} --value company=");

        var value = Assert.Single(fake.LastMemberUpdate!.Value.Request.Values!);
        Assert.Equal(("company", (object?)""), (value.Alias, value.Value));
    }

    [Fact]
    public async Task MemberUpdate_EmptyKey_IsRefusedBeforeTheClient()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, $"member update {Id} --value =Acme");

        Assert.Equal((1, false), (exit, fake.LastMemberUpdate.HasValue));
    }

    [Fact]
    public async Task MemberUpdate_AliasSetAndClearedInOneCall_IsRefusedBeforeTheClient()
    {
        // key= is a value for the key like any other, so it cannot be given beside a second one
        // for the same alias (#444): "set it" and "clear it" conflict.
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, $"member update {Id} --value company=Acme --value company=");

        Assert.Equal((1, false), (exit, fake.LastMemberUpdate.HasValue));
    }

    // -- domain bindings: key= removes the binding -----------------------------------

    [Fact]
    public async Task DomainSet_EmptyIsoCode_SendsNoBindingForThatHost()
    {
        var fake = new FakeUmbracoManagementClient();

        await Run(
            fake,
            $"content domain set {Id} --domain example.com=en-US --domain example.com/da="
        );

        Assert.Equal("example.com", Assert.Single(fake.LastDomains!.Domains).DomainName);
    }

    [Fact]
    public async Task DomainSet_HostBoundAndRemovedInOneCall_IsRefusedBeforeTheClient()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(
            fake,
            $"content domain set {Id} --domain example.com=en-US --domain Example.com="
        );

        Assert.Equal((1, false), (exit, fake.LastDomains is not null));
    }

    [Fact]
    public void Merge_EmptyIsoCode_RemovesTheCurrentBinding()
    {
        DomainBinding[] current =
        [
            new() { DomainName = "example.com", IsoCode = "en-US" },
            new() { DomainName = "example.com/da", IsoCode = "da-DK" },
        ];

        var merged = ContentDomainsCommand.Merge(
            current,
            [new DomainBinding { DomainName = "EXAMPLE.com/da", IsoCode = "" }]
        );

        Assert.Equal(["example.com"], merged.Select(b => b.DomainName));
    }

    [Fact]
    public void Merge_NewBinding_IsAddedAndTheOthersKept()
    {
        DomainBinding[] current = [new() { DomainName = "example.com", IsoCode = "en-US" }];

        var merged = ContentDomainsCommand.Merge(
            current,
            [new DomainBinding { DomainName = "example.com/da", IsoCode = "da-DK" }]
        );

        Assert.Equal(["example.com", "example.com/da"], merged.Select(b => b.DomainName));
    }

    [Fact]
    public void Merge_ReplaceWithAnEmptyIsoCode_DropsThatBinding()
    {
        var merged = ContentDomainsCommand.Merge(
            null,
            [
                new DomainBinding { DomainName = "example.com", IsoCode = "en-US" },
                new DomainBinding { DomainName = "example.com/da", IsoCode = "" },
            ]
        );

        Assert.Equal(["example.com"], merged.Select(b => b.DomainName));
    }
}
