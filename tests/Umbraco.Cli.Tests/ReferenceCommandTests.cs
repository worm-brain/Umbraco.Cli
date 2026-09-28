using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
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
        root.Add(ContentCommand.Build(executor));
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

        var exit = await Run(fake, "template delete blogPost --yes");

        Assert.Equal(0, exit);
        Assert.Equal([id], fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task MemberTypesDelete_ByAlias_ChecksAndDeletesTheResolvedType()
    {
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.MemberType, "siteMember")] = id;

        var exit = await Run(fake, "member-type delete siteMember --yes");

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

        var exit = await Run(fake, "member-type delete siteMember --yes");

        Assert.Equal(2, exit);
        Assert.Empty(fake.SchemaDeletedIds);
    }

    [Fact]
    public async Task UserGroupsGet_ByAlias_ReadsTheResolvedGroup()
    {
        // #217: user-group get blogEditors was a GUID parse error.
        var id = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.UserGroup, "blogEditors")] = id;
        fake.UserGroupList.Add(new UserGroupResponse { Id = id, Alias = "blogEditors" });

        var exit = await Run(fake, "user-group get blogEditors");

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task UserGroupsCreate_WithAStartNode_PassesIt()
    {
        var blog = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();

        await Run(
            fake,
            $"user-group create --alias blogEditors --name Blog --document-start-node {blog}"
        );

        Assert.Equal(blog, Assert.Single(fake.UserGroupsCreated).DocumentStartNode);
    }

    [Fact]
    public async Task MembersUpdate_GroupByName_SendsTheResolvedIds()
    {
        // #212: member update --group Subscribers was a GUID parse error.
        var subscribers = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.MemberGroup, "Subscribers")] = subscribers;

        var exit = await Run(fake, $"member update {Guid.NewGuid()} --group Subscribers");

        Assert.Equal(0, exit);
        Assert.Equal([subscribers], fake.LastMemberUpdate!.Value.Request.Groups!);
    }

    [Fact]
    public async Task MembersUpdate_UnknownGroup_UpdatesNothing()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, $"member update {Guid.NewGuid()} --group Nope");

        Assert.Equal((1, false), (exit, fake.LastMemberUpdate.HasValue));
    }

    [Fact]
    public async Task DictionaryMove_ByKeys_MovesTheResolvedItemUnderTheResolvedParent()
    {
        // #211: moving 16 items needed a tree lookup for every id, and every parent's id.
        var (tags, blog) = (Guid.NewGuid(), Guid.NewGuid());
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DictionaryItem, "Blog.Tags")] = tags;
        fake.References[(EntityKind.DictionaryItem, "Blog")] = blog;

        var exit = await Run(fake, "dictionary move Blog.Tags --target Blog");

        Assert.Equal(0, exit);
        Assert.Equal([(tags, (Guid?)blog)], fake.DictionaryItemsMoved);
    }

    [Fact]
    public async Task DictionaryCreate_UnderAParentKey_CreatesUnderTheResolvedParent()
    {
        var blog = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DictionaryItem, "Blog")] = blog;

        await Run(fake, "dictionary create --key Blog.MinRead --parent Blog");

        Assert.Equal(blog, Assert.Single(fake.DictionaryItemsCreated).Parent!.Id);
    }

    [Fact]
    public async Task ContentPublish_CommaSeparatedCultures_PublishesEachCulture()
    {
        // #231: en-US,da-DK was sent as one culture and Umbraco answered 400.
        var fake = new FakeUmbracoManagementClient
        {
            PublishContentHandler = _ => UmbracoResponse<Empty>.Success(Empty.Value),
            PublishCulturesHandler = _ =>
                UmbracoResponse<IReadOnlyList<string>>.Success(["en-US", "da-DK", "ja-JP"]),
        };

        await Run(fake, $"content publish {Guid.NewGuid()} --culture en-US,da-DK");

        Assert.Equal(["en-US", "da-DK"], Assert.Single(fake.StateCalls).Cultures!);
    }

    [Fact]
    public async Task ContentSort_ByName_SortsTheChildrenAToZ()
    {
        // #232: sorting by a field used to need a get-per-child script.
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var fake = new FakeUmbracoManagementClient();
        fake.ContentChildren.Add(new ContentItemResponse { Id = a, Name = "Zebra" });
        fake.ContentChildren.Add(new ContentItemResponse { Id = b, Name = "Aardvark" });

        var exit = await Run(fake, "content sort --by name");

        Assert.Equal(0, exit);
        Assert.Equal([b, a], Assert.Single(fake.ContentSorted).OrderedChildIds);
    }

    [Fact]
    public async Task ContentSort_ByPublishDateDescending_ReadsEachChildAndPutsNewestFirst()
    {
        // The document tree carries no dates, so each child is read.
        var (older, newer) = (Guid.NewGuid(), Guid.NewGuid());
        var fake = new FakeUmbracoManagementClient();
        foreach (var (id, day) in new[] { (older, 1), (newer, 20) })
        {
            fake.ContentChildren.Add(new ContentItemResponse { Id = id, Name = $"post {day}" });
            fake.ContentById[id] = new ContentItemResponse
            {
                Id = id,
                Variants =
                [
                    new ContentVariantResponse
                    {
                        PublishDate = new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.Zero),
                    },
                ],
            };
        }

        var exit = await Run(fake, "content sort --by publishDate --desc");

        Assert.Equal(0, exit);
        Assert.Equal([newer, older], Assert.Single(fake.ContentSorted).OrderedChildIds);
    }

    [Fact]
    public async Task TemplatesDelete_UnknownAlias_FailsWithoutDeleting()
    {
        var fake = new FakeUmbracoManagementClient();

        var exit = await Run(fake, "template delete nope --yes");

        Assert.Equal((1, 0), (exit, fake.SchemaDeletedIds.Count));
    }
}
