using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the user, user-group and user-data write paths put on the wire (#187 Phase 2).
/// <para>
/// Two things here are unusual enough to be worth pinning: several of these DELETE with a request
/// body, which is easy to lose in a refactor to a bodiless overload; and an invite whose whole
/// effect is the <c>userGroupIds</c> array, so dropping it silently invites someone with no
/// permissions at all.
/// </para>
/// </summary>
public class UserAdminWireTests
{
    // ── invite ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteUserAsync_SendsTheGroupIds()
    {
        var group = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .InviteUserAsync(
                new InviteUserRequest
                {
                    Email = "new@example.com",
                    Name = "New User",
                    UserGroupIds = [new ReferenceById { Id = group }],
                },
                CancellationToken.None
            );

        // Losing this array invites a user who can see nothing, with no error to show for it.
        var body = handler.BodyOf(HttpMethod.Post, "/user/invite");
        Assert.Equal("new@example.com", body["email"]!.GetValue<string>());
        var ids = Assert.Single(body["userGroupIds"]!.AsArray());
        Assert.Equal(group.ToString(), ids!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InviteUserAsync_NoMessage_OmitsIt()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .InviteUserAsync(
                new InviteUserRequest
                {
                    Email = "new@example.com",
                    Name = "New User",
                    UserGroupIds = [new ReferenceById { Id = Guid.NewGuid() }],
                },
                CancellationToken.None
            );

        Assert.Null(handler.BodyOf(HttpMethod.Post, "/user/invite")["message"]);
    }

    // ── user groups ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserGroupAsync_SendsAliasNameAndCollections()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateUserGroupAsync(
                new CreateUserGroupRequest
                {
                    Alias = "editors",
                    Name = "Editors",
                    Sections = ["Umb.Section.Content", "Umb.Section.Media"],
                    Languages = ["en-US"],
                    FallbackPermissions = ["Umb.Document.Read"],
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/user-group");
        Assert.Equal("editors", body["alias"]!.GetValue<string>());
        Assert.Equal(2, body["sections"]!.AsArray().Count);
        Assert.Equal("en-US", Assert.Single(body["languages"]!.AsArray())!.GetValue<string>());
        Assert.Single(body["fallbackPermissions"]!.AsArray());
    }

    [Fact]
    public async Task CreateUserGroupAsync_NoSections_SendsAnEmptyArrayNotAMissingKey()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateUserGroupAsync(
                new CreateUserGroupRequest { Alias = "readers", Name = "Readers" },
                CancellationToken.None
            );

        // An empty collection and an absent key are different to the server; a group with no
        // sections is a real, expressible state.
        var body = handler.BodyOf(HttpMethod.Post, "/user-group");
        Assert.True(body.ContainsKey("sections"));
        Assert.Empty(body["sections"]!.AsArray());
    }

    [Fact]
    public async Task AddUsersToGroupAsync_PostsTheUserIds()
    {
        var group = Guid.NewGuid();
        var user = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).AddUsersToGroupAsync(group, [user], CancellationToken.None);

        // The body is a bare array of {id}, not an object wrapping one.
        var body = handler.BodyNodeOf(HttpMethod.Post, $"/user-group/{group}/users").AsArray();
        Assert.Equal(user.ToString(), Assert.Single(body)!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task RemoveUsersFromGroupAsync_DeletesWithABody()
    {
        var group = Guid.NewGuid();
        var user = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).RemoveUsersFromGroupAsync(group, [user], CancellationToken.None);

        // A DELETE that carries a body: refactoring this to a bodiless overload would silently
        // remove nobody. The body is a bare array of {id}.
        var body = handler.BodyNodeOf(HttpMethod.Delete, $"/user-group/{group}/users").AsArray();
        Assert.Equal(user.ToString(), Assert.Single(body)!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task DeleteUserGroupsAsync_DeletesWithABodyOfIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteUserGroupsAsync([first, second], CancellationToken.None);

        var body = handler.BodyOf(HttpMethod.Delete, "/user-group");
        Assert.Equal(2, body["userGroupIds"]!.AsArray().Count);
    }

    [Fact]
    public async Task DeleteUserGroupAsync_DeletesTheGroup()
    {
        var id = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteUserGroupAsync(id, CancellationToken.None);

        handler.AssertRequested(HttpMethod.Delete, $"/user-group/{id}");
    }

    // ── user data ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserDataAsync_SendsGroupIdentifierAndValue()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateUserDataAsync(
                new CreateUserDataRequest
                {
                    Group = "ui",
                    Identifier = "theme",
                    Value = "dark",
                },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, "/user-data");
        Assert.Equal("ui", body["group"]!.GetValue<string>());
        Assert.Equal("theme", body["identifier"]!.GetValue<string>());
        Assert.Equal("dark", body["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateUserDataAsync_PutsToTheCollectionWithTheKeyInTheBody()
    {
        var key = Guid.NewGuid();
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .UpdateUserDataAsync(
                new UpdateUserDataRequest
                {
                    Key = key,
                    Group = "ui",
                    Identifier = "theme",
                    Value = "light",
                },
                CancellationToken.None
            );

        // Unlike every other update, this PUTs to the collection: the key identifies the row from
        // inside the body, so a refactor that moved it into the path would 404.
        var body = handler.BodyOf(HttpMethod.Put, "/user-data");
        Assert.Equal(key.ToString(), body["key"]!.GetValue<string>());
        Assert.Equal("light", body["value"]!.GetValue<string>());
    }
}
