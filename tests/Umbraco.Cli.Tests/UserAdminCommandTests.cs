using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.UserData;
using Umbraco.Cli.Commands.UserGroups;
using Umbraco.Cli.Commands.Users;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Command-layer behaviour of the user-administration nouns (#109): option mapping into the client
/// request (repeatable flags, the deferred-permissions defaults), and the confirmation gating on
/// the destructive verbs. Client HTTP behaviour is covered separately by the client tests.
/// </summary>
[Collection("ConsoleCapture")]
public class UserAdminCommandTests
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

    private static RootCommand BuildRoot(
        IUmbracoManagementClient client,
        IConfirmationPrompt prompt
    )
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
        var executor = new CommandExecutor(factory, prompt);
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(UserGroupsCommand.Build(executor));
        root.Add(UserDataCommand.Build(executor));
        root.Add(UsersCommand.Build(executor));
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

    // ── user-group: option mapping ─────────────────────────────────────────────

    [Fact]
    public async Task UserGroupsCreate_MapsRepeatableAndFlagOptions()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));

        var exit = await Run(
            root,
            $"{Auth} user-group create --alias editors --name Editors "
                + "--section Umb.Section.Content --section Umb.Section.Media "
                + "--culture en-US --fallback-permission Umb.Document.Read "
                + "--has-access-to-all-languages --document-root-access"
        );

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.UserGroupsCreated);
        Assert.Equal("editors", created.Alias);
        Assert.Equal(["Umb.Section.Content", "Umb.Section.Media"], created.Sections);
        Assert.Equal(["en-US"], created.Languages);
        Assert.Equal(["Umb.Document.Read"], created.FallbackPermissions);
        Assert.True(created.HasAccessToAllLanguages);
        Assert.True(created.DocumentRootAccess);
        Assert.False(created.MediaRootAccess); // flag not passed -> default
    }

    [Fact]
    public async Task UserGroupsDelete_SeveralIds_DeletesThemInOneCall()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} --yes user-group delete {a} {b}");

        Assert.Equal(0, exit);
        var ids = Assert.Single(fake.UserGroupsBulkDeleted);
        Assert.Equal([a, b], ids);
    }

    [Fact]
    public async Task UserGroupsAddUsers_MapsGroupAndUserIds()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));
        var group = Guid.NewGuid();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} user-group add-users {group} --user {u1} --user {u2}");

        Assert.Equal(0, exit);
        var (groupId, userIds) = Assert.Single(fake.UserGroupUsersAdded);
        Assert.Equal(group, groupId);
        Assert.Equal([u1, u2], userIds);
    }

    // ── user-group: confirmation gating ────────────────────────────────────────

    [Fact]
    public async Task UserGroupsDelete_NonInteractiveWithoutYes_AbortsAndDoesNotDelete()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));

        var exit = await Run(root, $"{Auth} user-group delete {Guid.NewGuid()}");

        Assert.Equal(2, exit); // aborted before running (confirmation required, non-interactive)
        Assert.Empty(fake.UserGroupsDeleted);
    }

    [Fact]
    public async Task UserGroupsDelete_WithYes_Deletes()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));
        var id = Guid.NewGuid();

        var exit = await Run(root, $"{Auth} --yes user-group delete {id}");

        Assert.Equal(0, exit);
        Assert.Equal(id, Assert.Single(fake.UserGroupsDeleted));
    }

    // ── user-data: option mapping + gating ──────────────────────────────────────

    [Fact]
    public async Task UserDataCreate_MapsFields()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));

        var exit = await Run(
            root,
            $"{Auth} user-data create --group myGroup --identifier theme --data dark"
        );

        Assert.Equal(0, exit);
        var created = Assert.Single(fake.UserDataCreated);
        Assert.Equal("myGroup", created.Group);
        Assert.Equal("theme", created.Identifier);
        Assert.Equal("dark", created.Value);
    }

    [Fact]
    public async Task UserDataUpdate_MapsKeyAndFields()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));
        var key = Guid.NewGuid();
        fake.UserDataList.Add(
            new UserDataResponse
            {
                Key = key,
                Group = "g",
                Identifier = "i",
                Value = "dark",
            }
        );

        var exit = await Run(
            root,
            $"{Auth} user-data update {key} --group g --identifier i --data light"
        );

        Assert.Equal(0, exit);
        var updated = Assert.Single(fake.UserDataUpdated);
        Assert.Equal(key, updated.Key);
        Assert.Equal("light", updated.Value);
    }

    [Fact]
    public async Task UserDataUpdate_OmittedOptions_KeepTheirValues()
    {
        // docs/conventions.md 5.1: update merges, so only --data changes here.
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));
        var key = Guid.NewGuid();
        fake.UserDataList.Add(
            new UserDataResponse
            {
                Key = key,
                Group = "prefs",
                Identifier = "theme",
                Value = "dark",
            }
        );

        await Run(root, $"{Auth} user-data update {key} --data light");

        var updated = Assert.Single(fake.UserDataUpdated);
        Assert.Equal(
            ("prefs", "theme", "light"),
            (updated.Group, updated.Identifier, updated.Value)
        );
    }

    [Fact]
    public async Task UserDataDelete_NonInteractiveWithoutYes_AbortsAndDoesNotDelete()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));

        var exit = await Run(root, $"{Auth} user-data delete {Guid.NewGuid()}");

        Assert.Equal(2, exit);
        Assert.Empty(fake.UserDataDeleted);
    }

    // ── user invite (#215) ─────────────────────────────────────────────────────
    // The client resolves the group references and defaults the userName; see the invite wire
    // tests. The command's job is to pass the options through and require a group.

    [Fact]
    public async Task UsersInvite_PassesGroupReferencesAndUserNameThrough()
    {
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));

        var exit = await Run(
            root,
            $"{Auth} user invite --email a@example.com --name A --group editor --group Translators --username alice"
        );

        Assert.Equal(0, exit);
        Assert.Equal(["editor", "Translators"], fake.LastInvite!.UserGroups);
        Assert.Equal("alice", fake.LastInvite.UserName);
    }

    [Fact]
    public async Task UsersInvite_NoGroup_IsRefusedAtParseTime()
    {
        // Umbraco needs at least one group, and an empty list was the old silent default.
        var fake = new FakeUmbracoManagementClient();
        var root = BuildRoot(fake, new Prompt(interactive: false, answer: true));

        var exit = await Run(root, $"{Auth} user invite --email a@example.com --name A");

        Assert.NotEqual(0, exit);
        Assert.Null(fake.LastInvite);
    }
}
