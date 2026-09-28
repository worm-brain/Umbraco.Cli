using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// What the user reads and writes put on the wire and make of the answers (#214, #216): reads are
/// labelled with their groups; create sets the password with a second call and rolls back when it
/// is rejected; update merges its PUT over the current user and sends each lifecycle change to its
/// own endpoint; users resolve by email and username.
/// </summary>
public class UserWireTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Editors = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Translators = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StartNode = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>The user as <c>GET user/{id}</c> returns it: in the editor group, with a start node.</summary>
    private static readonly string Jane = $$"""
        { "id": "{{UserId}}", "email": "jane@example.com", "userName": "jane@example.com",
          "name": "Jane", "userGroupIds": [ { "id": "{{Editors}}" } ], "languageIsoCode": "en-US",
          "documentStartNodeIds": [ { "id": "{{StartNode}}" } ], "hasDocumentRootAccess": false,
          "mediaStartNodeIds": [], "hasMediaRootAccess": true, "state": "Active", "kind": "Default",
          "isAdmin": false, "failedLoginAttempts": 2, "avatarUrls": [],
          "createDate": "2026-09-01T00:00:00Z", "updateDate": "2026-09-02T00:00:00Z" }
        """;

    /// <summary>A user-group list with an "editor" and a "translator" group.</summary>
    private const string TwoGroups = """
        {"total":2,"items":[
          {"id":"11111111-1111-1111-1111-111111111111","alias":"editor","name":"Editors",
           "sections":["Umb.Section.Content","Umb.Section.Media"],"languages":["en-US","da-DK"]},
          {"id":"22222222-2222-2222-2222-222222222222","alias":"translator","name":"Translators",
           "sections":["Umb.Section.Translation"]}]}
        """;

    /// <summary>A handler serving <see cref="Jane"/> and <see cref="TwoGroups"/>; writes succeed empty.</summary>
    private static RoutingHandler JaneAndGroups() =>
        new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get && Path(r).EndsWith("/user-group"),
                HttpStatusCode.OK,
                TwoGroups
            )
            .When(
                r => r.Method == HttpMethod.Get && Path(r).EndsWith($"/user/{UserId}"),
                HttpStatusCode.OK,
                Jane
            )
            .When(_ => true, HttpStatusCode.OK, "");

    private static string Path(HttpRequestMessage r) => r.RequestUri!.AbsolutePath;

    /// <summary>A ProblemDetails body for a rejected password.</summary>
    private const string WeakPassword = """
        { "title": "Password does not meet the requirements", "status": 400 }
        """;

    // ── reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserByIdAsync_UserInAGroup_LabelsTheGroupWithAliasNameAndSections()
    {
        var result = await Wire.Client(JaneAndGroups())
            .GetUserByIdAsync(UserId, CancellationToken.None);

        Assert.Equal(
            (
                new UserGroupRef
                {
                    Id = Editors,
                    Alias = "editor",
                    Name = "Editors",
                },
                "Umb.Section.Content,Umb.Section.Media"
            ),
            (Assert.Single(result.Data!.UserGroups), string.Join(",", result.Data.Sections))
        );
    }

    [Fact]
    public async Task GetUserByIdAsync_MapsStartNodesLanguageAndLoginRecord()
    {
        var result = await Wire.Client(JaneAndGroups())
            .GetUserByIdAsync(UserId, CancellationToken.None);

        var user = result.Data!;
        Assert.Equal(
            ((Guid?)StartNode, "en-US", true, "Default", 2),
            (
                user.DocumentStartNodes.SingleOrDefault(),
                user.LanguageIsoCode,
                user.MediaRootAccess,
                user.Kind,
                user.FailedLoginAttempts
            )
        );
    }

    [Fact]
    public async Task GetUserByIdAsync_UserInAGroup_ListsTheGroupsContentLanguages()
    {
        // #356: content languages come from the groups, like sections.
        var result = await Wire.Client(JaneAndGroups())
            .GetUserByIdAsync(UserId, CancellationToken.None);

        Assert.Equal(
            ("en-US,da-DK", false),
            (string.Join(",", result.Data!.Languages), result.Data.HasAccessToAllLanguages)
        );
    }

    [Fact]
    public async Task GetUserByIdAsync_GroupWithAllLanguages_HasAccessToAllLanguages()
    {
        var handler = new RoutingHandler()
            .When(
                r => Path(r).EndsWith("/user-group"),
                HttpStatusCode.OK,
                $$"""{"total":1,"items":[{"id":"{{Editors}}","alias":"editor","name":"Editors","hasAccessToAllLanguages":true}]}"""
            )
            .When(_ => true, HttpStatusCode.OK, Jane);

        var result = await Wire.Client(handler).GetUserByIdAsync(UserId, CancellationToken.None);

        Assert.True(result.Data!.HasAccessToAllLanguages);
    }

    [Fact]
    public async Task GetUserByIdAsync_GroupListForbidden_KeepsTheGroupIdWithoutLabels()
    {
        // An API user without the Users section cannot read groups; the user read must not fail.
        var handler = new RoutingHandler()
            .When(r => Path(r).EndsWith("/user-group"), HttpStatusCode.Forbidden, "")
            .When(_ => true, HttpStatusCode.OK, Jane);

        var result = await Wire.Client(handler).GetUserByIdAsync(UserId, CancellationToken.None);

        Assert.Equal(new UserGroupRef { Id = Editors }, Assert.Single(result.Data!.UserGroups));
    }

    // ── create ────────────────────────────────────────────────────────────────

    private static CreateUserRequest NewUser(string? password = null) =>
        new()
        {
            Id = UserId,
            Email = "new@example.com",
            Name = "New User",
            UserGroups = ["editor"],
            Password = password,
        };

    [Fact]
    public async Task CreateUserAsync_PostsTheUserWithResolvedGroupsAndEmailAsUserName()
    {
        var handler = Wire.Routed(("/user-group", TwoGroups));

        await Wire.Client(handler).CreateUserAsync(NewUser(), CancellationToken.None);

        var body = handler.BodyOf(HttpMethod.Post, "/user");
        Assert.Equal(
            ("new@example.com", Editors.ToString(), "Default"),
            (
                body["userName"]!.GetValue<string>(),
                Assert.Single(body["userGroupIds"]!.AsArray())!["id"]!.GetValue<string>(),
                body["kind"]!.GetValue<string>()
            )
        );
    }

    [Fact]
    public async Task CreateUserAsync_WithPassword_SetsItOnTheNewUser()
    {
        var handler = Wire.Routed(("/user-group", TwoGroups));

        var result = await Wire.Client(handler)
            .CreateUserAsync(NewUser("S3cure!Passw0rd"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var body = handler.BodyOf(HttpMethod.Post, $"/user/{UserId}/change-password");
        Assert.Equal("S3cure!Passw0rd", body["newPassword"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateUserAsync_WithoutPassword_SendsNoPasswordCall()
    {
        var handler = Wire.Routed(("/user-group", TwoGroups));

        await Wire.Client(handler).CreateUserAsync(NewUser(), CancellationToken.None);

        handler.AssertNoRequest(HttpMethod.Post, "/change-password");
    }

    [Fact]
    public async Task CreateUserAsync_PasswordRejected_DeletesTheNewUserAgain()
    {
        var handler = new RoutingHandler()
            .When(r => Path(r).EndsWith("/user-group"), HttpStatusCode.OK, TwoGroups)
            .When(
                r => Path(r).EndsWith("/change-password"),
                HttpStatusCode.BadRequest,
                WeakPassword
            )
            .When(_ => true, HttpStatusCode.OK, "");

        var result = await Wire.Client(handler)
            .CreateUserAsync(NewUser("weak"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        handler.AssertRequested(HttpMethod.Delete, $"/user/{UserId}");
        Assert.Contains("deleted again", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_PasswordRejectedAndRollbackFails_NamesTheLeftoverUser()
    {
        var handler = new RoutingHandler()
            .When(r => Path(r).EndsWith("/user-group"), HttpStatusCode.OK, TwoGroups)
            .When(
                r => Path(r).EndsWith("/change-password"),
                HttpStatusCode.BadRequest,
                WeakPassword
            )
            .When(r => r.Method == HttpMethod.Delete, HttpStatusCode.InternalServerError, "")
            .When(_ => true, HttpStatusCode.OK, "");

        var result = await Wire.Client(handler)
            .CreateUserAsync(NewUser("weak"), CancellationToken.None);

        Assert.Contains($"umbraco user delete {UserId}", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateUserAsync_UnknownGroup_CreatesNothing()
    {
        var handler = Wire.Routed(("/user-group", TwoGroups));

        var result = await Wire.Client(handler)
            .CreateUserAsync(NewUser() with { UserGroups = ["nope"] }, CancellationToken.None);

        Assert.Equal(FailureCategory.InvalidArgument, result.Category);
        handler.AssertNoRequest(HttpMethod.Post, "/user");
    }

    // ── update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUserAsync_NewName_KeepsTheRestOfTheProfile()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Name = "Jane Roe" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, $"/user/{UserId}");
        Assert.Equal(
            ("Jane Roe", "jane@example.com", Editors.ToString(), StartNode.ToString(), true),
            (
                body["name"]!.GetValue<string>(),
                body["email"]!.GetValue<string>(),
                body["userGroupIds"]![0]!["id"]!.GetValue<string>(),
                body["documentStartNodeIds"]![0]!["id"]!.GetValue<string>(),
                body["hasMediaRootAccess"]!.GetValue<bool>()
            )
        );
    }

    [Fact]
    public async Task UpdateUserAsync_NewEmailWhenUserNameIsTheEmail_MovesTheUserNameToo()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Email = "jane.roe@example.com" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, $"/user/{UserId}");
        Assert.Equal("jane.roe@example.com", body["userName"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateUserAsync_NewEmailAndExplicitUserName_KeepsTheGivenUserName()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Email = "jane.roe@example.com", UserName = "jroe" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Put, $"/user/{UserId}");
        Assert.Equal("jroe", body["userName"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateUserAsync_Groups_ReplaceTheUsersGroups()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { UserGroups = ["translator"] },
                CancellationToken.None
            );

        var groups = handler.BodyOf(HttpMethod.Put, $"/user/{UserId}")["userGroupIds"]!.AsArray();
        Assert.Equal(Translators.ToString(), Assert.Single(groups)!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateUserAsync_OnlyLifecycleOptions_SendsNoPut()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Unlock = true },
                CancellationToken.None
            );

        handler.AssertNoRequest(HttpMethod.Put, $"/user/{UserId}");
    }

    [Fact]
    public async Task UpdateUserAsync_Unlock_PostsTheUserToUnlock()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Unlock = true },
                CancellationToken.None
            );

        var ids = handler.BodyOf(HttpMethod.Post, "/user/unlock")["userIds"]!.AsArray();
        Assert.Equal(UserId.ToString(), Assert.Single(ids)!["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true, "/user/disable")]
    [InlineData(false, "/user/enable")]
    public async Task UpdateUserAsync_Disabled_PostsToTheMatchingEndpoint(
        bool disabled,
        string path
    )
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Disabled = disabled },
                CancellationToken.None
            );

        handler.AssertRequested(HttpMethod.Post, path);
    }

    [Fact]
    public async Task UpdateUserAsync_NewPassword_PostsChangePassword()
    {
        var handler = JaneAndGroups();

        await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { NewPassword = "S3cure!Passw0rd" },
                CancellationToken.None
            );

        var body = handler.BodyOf(HttpMethod.Post, $"/user/{UserId}/change-password");
        Assert.Equal("S3cure!Passw0rd", body["newPassword"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateUserAsync_PasswordFailsAfterProfile_SaysTheProfileWasApplied()
    {
        var handler = new RoutingHandler()
            .When(
                r => Path(r).EndsWith("/change-password"),
                HttpStatusCode.BadRequest,
                WeakPassword
            )
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, Jane)
            .When(_ => true, HttpStatusCode.OK, "");

        var result = await Wire.Client(handler)
            .UpdateUserAsync(
                UserId,
                new UpdateUserRequest { Name = "Jane Roe", NewPassword = "weak" },
                CancellationToken.None
            );

        Assert.StartsWith("Applied the profile, but the password failed", result.ErrorMessage);
    }

    // ── delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUsersAsync_SendsEveryIdInTheBody()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler).DeleteUsersAsync([UserId, Editors], CancellationToken.None);

        var ids = handler.BodyOf(HttpMethod.Delete, "/user")["userIds"]!
            .AsArray()
            .Select(i => i!["id"]!.GetValue<string>());
        Assert.Equal([UserId.ToString(), Editors.ToString()], ids);
    }

    /// <summary>Umbraco's refusal to delete a user who has signed in.</summary>
    private const string LoginHistory = """
        { "title": "Cannot delete user", "status": 400,
          "detail": "This user has logged in and may be referenced by audit logs or content history.",
          "operationStatus": "CannotDeleteUserWithLoginHistory" }
        """;

    /// <summary>
    /// A handler that refuses every delete with <paramref name="refusal"/>, where Jane
    /// (<see cref="UserId"/>) has signed in and the user with id <see cref="Editors"/> has not.
    /// </summary>
    /// <param name="refusal">The 400 body the delete gets.</param>
    /// <returns>The handler.</returns>
    private static RoutingHandler DeleteRefused(string refusal = LoginHistory) =>
        new RoutingHandler()
            .When(r => r.Method == HttpMethod.Delete, HttpStatusCode.BadRequest, refusal)
            .When(
                r => Path(r).EndsWith($"/user/{UserId}"),
                HttpStatusCode.OK,
                Jane.Replace(
                    "\"failedLoginAttempts\": 2,",
                    "\"failedLoginAttempts\": 2, \"lastLoginDate\": \"2026-09-20T00:00:00Z\","
                )
            )
            .When(
                r => Path(r).EndsWith($"/user/{Editors}"),
                HttpStatusCode.OK,
                $$"""{ "id": "{{Editors}}", "email": "never@example.com", "name": "Never" }"""
            );

    [Fact]
    public async Task DeleteUsersAsync_RefusedForLoginHistory_NamesOnlyTheUserWhoSignedIn()
    {
        // #355: Umbraco says "This user has logged in" without saying which of several.
        var result = await Wire.Client(DeleteRefused())
            .DeleteUsersAsync([UserId, Editors], CancellationToken.None);

        Assert.Equal(
            (true, false),
            (
                result.ErrorMessage!.Contains($"jane@example.com ({UserId})"),
                result.ErrorMessage.Contains("never@example.com")
            )
        );
    }

    [Fact]
    public async Task DeleteUserAsync_RefusedForLoginHistory_SuggestsDisablingTheUser()
    {
        var result = await Wire.Client(DeleteRefused())
            .DeleteUserAsync(UserId, CancellationToken.None);

        Assert.Contains($"umbraco user update {UserId} --disabled", result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteUsersAsync_OtherRefusal_ReadsNoUsers()
    {
        var handler = DeleteRefused("""{ "title": "Forbidden", "status": 400 }""");

        await Wire.Client(handler).DeleteUsersAsync([UserId, Editors], CancellationToken.None);

        Assert.DoesNotContain(handler.Recordings, r => r.Method == HttpMethod.Get);
    }

    // ── resolution ────────────────────────────────────────────────────────────

    private const string OneUserList = """
        {"total":1,"items":[{"id":"aaaaaaaa-0000-0000-0000-000000000001",
          "email":"jane@example.com","userName":"jane","name":"Jane"}]}
        """;

    [Theory]
    [InlineData("jane@example.com")]
    [InlineData("JANE")]
    public async Task ResolveIdAsync_UserEmailOrUserName_ResolvesToTheUser(string reference)
    {
        var handler = Wire.Returning(OneUserList);

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.User, reference, CancellationToken.None);

        Assert.Equal(UserId, result.Data);
    }

    [Fact]
    public async Task ResolveIdAsync_UnknownUser_FailsNamingTheListCommand()
    {
        var handler = Wire.Returning(OneUserList);

        var result = await Wire.Client(handler)
            .ResolveIdAsync(EntityKind.User, "bob@example.com", CancellationToken.None);

        Assert.Contains("umbraco user list", result.ErrorMessage);
    }
}
