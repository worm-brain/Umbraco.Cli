using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Commands.MediaTypes;
using Umbraco.Cli.Commands.MemberGroups;
using Umbraco.Cli.Commands.Members;
using Umbraco.Cli.Commands.MemberTypes;
using Umbraco.Cli.Commands.Templates;
using Umbraco.Cli.Commands.UserGroups;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Commands take an item's id <i>or</i> its alias, name or key (#250 Phase 3) and act on the id
/// it resolves to; a reference that resolves to nothing fails before anything is written.
/// </summary>
[Collection("ConsoleCapture")]
public class ReferenceCommandTests
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

    private sealed class NonInteractivePrompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => false;
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
        var executor = new CommandExecutor(factory, new NonInteractivePrompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(TemplatesCommand.Build(executor));
        root.Add(MediaTypesCommand.Build(executor));
        root.Add(MemberTypesCommand.Build(executor));
        root.Add(UserGroupsCommand.Build(executor));
        root.Add(MemberGroupsCommand.Build(executor));
        root.Add(DictionaryCommand.Build(executor));
        root.Add(MembersCommand.Build(executor));
        return root;
    }

    /// <summary>Runs a command line and returns its exit code; stdout/stderr are swallowed.</summary>
    private static async Task<int> Run(IUmbracoManagementClient client, string args)
    {
        var (out_, err) = (Console.Out, Console.Error);
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        try
        {
            return await BuildRoot(client)
                .Parse($"--host https://x --token t --output json {args}")
                .InvokeAsync();
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }
    }

    [Fact]
    public async Task TemplatesDelete_ByAlias_DeletesTheResolvedTemplate()
    {
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.Template, "blogPost")] = id;

        var exit = await Run(fake, "templates delete blogPost --yes");

        Assert.Equal(0, exit);
        Assert.Equal([id], fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task MemberTypesDelete_ByAlias_ChecksAndDeletesTheResolvedType()
    {
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.MemberType, "siteMember")] = id;

        var exit = await Run(fake, "member-types delete siteMember --yes");

        Assert.Equal(0, exit);
        Assert.Equal([id], fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task MemberTypesDelete_ByAliasOfATypeWithMembers_IsRefused()
    {
        // The in-use guard must count the members of the type the alias resolves to.
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.MemberType, "siteMember")] = id;
        fake.MemberCountsByType[id] = 3;

        var exit = await Run(fake, "member-types delete siteMember --yes");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task UserGroupsGet_ByAlias_ReadsTheResolvedGroup()
    {
        // #217: user-groups get blogEditors was a GUID parse error.
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.UserGroup, "blogEditors")] = id;
        fake.UserGroupList.Add(new UserGroupResponse { Id = id, Alias = "blogEditors" });

        var exit = await Run(fake, "user-groups get blogEditors");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task UserGroupsCreate_WithAStartNode_PassesIt()
    {
        var blog = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();

        await Run(
            fake,
            $"user-groups create --alias blogEditors --name Blog --document-start-node {blog}"
        );

        Assert.Equal(blog, Assert.Single(fake.UserGroupsCreated).DocumentStartNode);
    }

    [Fact]
    public async Task TemplatesDelete_UnknownAlias_FailsWithoutDeleting()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, "templates delete nope --yes");

        Assert.Equal((1, 0), (exit, fake.SchemaDeletedIds.Count));
    }
}
