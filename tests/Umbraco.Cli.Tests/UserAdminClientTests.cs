using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the user-administration client methods (#109): user-group CRUD, bulk delete and
/// membership hit the right endpoints and verbs; user-data CRUD round-trips group/identifier/value
/// and passes the list filters. Uses a recording stub over the real client.
/// </summary>
public class UserAdminClientTests
{
    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (UmbracoManagementClient Client, StubHandler Handler) ClientReturning(
        string json,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        var handler = new StubHandler(json, status);
        var client = new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
        return (client, handler);
    }

    // ── User groups ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserGroupAsync_PostsFieldsAndEchoesId()
    {
        // The create is followed by a read-back GET (#354), so the POST is the first request.
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.Created, "");
        var id = Guid.NewGuid();

        var result = await Wire.Client(handler)
            .CreateUserGroupAsync(
                new CreateUserGroupRequest
                {
                    Id = id,
                    Alias = "editors",
                    Name = "Editors",
                    Sections = ["Umb.Section.Content"],
                },
                CancellationToken.None
            );

        var post = handler.Recordings[0];
        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.EndsWith("/user-group", post.Uri.AbsolutePath);
        Assert.Contains("editors", post.Body);
        Assert.Contains("Umb.Section.Content", post.Body);
        Assert.Equal(id, result.Data!.Id); // client-supplied id echoed
    }

    [Fact]
    public async Task CreateUserGroupAsync_ReadsTheGroupBack_SoServerFieldsMatchGet()
    {
        // #354: the create echoed the request, so isDeletable/aliasCanBeChanged were false and
        // description null where `get` says true and "".
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""{ "id": "{{id}}", "alias": "editors", "name": "Editors", "description": "", "isDeletable": true, "aliasCanBeChanged": true }"""
            )
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateUserGroupAsync(
                new CreateUserGroupRequest
                {
                    Id = id,
                    Alias = "editors",
                    Name = "Editors",
                },
                CancellationToken.None
            );

        Assert.Equal(
            (true, true, ""),
            (result.Data!.IsDeletable, result.Data.AliasCanBeChanged, result.Data.Description)
        );
    }

    [Fact]
    public async Task CreateUserGroupAsync_ReadBackFails_StillSucceedsWithTheRequest()
    {
        // The group was created; a failed read-back must not turn that into an error.
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.InternalServerError, "")
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateUserGroupAsync(
                new CreateUserGroupRequest { Alias = "editors", Name = "Editors" },
                CancellationToken.None
            );

        Assert.Equal((true, "editors"), (result.IsSuccess, result.Data!.Alias));
    }

    [Fact]
    public async Task UpdateUserGroupAsync_PutsToByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.UpdateUserGroupAsync(
            id,
            new UpdateUserGroupRequest { Alias = "editors", Name = "Renamed" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Contains($"user-group/{id}", handler.LastUri!.AbsoluteUri);
        Assert.Contains("Renamed", handler.LastBody);
    }

    [Fact]
    public async Task UpdateUserGroupAsync_KeepsTheGroupsGranularPermissions()
    {
        // #111: the PUT replaces the whole group, so an update that gives no permissions must
        // carry the current ones through rather than wipe them.
        var id = Guid.NewGuid();
        var node = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""
                { "id": "{{id}}", "alias": "editors", "name": "Editors", "sections": [], "languages": [],
                  "fallbackPermissions": [], "hasAccessToAllLanguages": false,
                  "documentRootAccess": false, "mediaRootAccess": false,
                  "permissions": [ { "$type": "DocumentPermissionPresentationModel",
                                     "document": { "id": "{{node}}" }, "verbs": ["Umb.Document.Read"] } ] }
                """
            )
            .When(_ => true, HttpStatusCode.OK, "");

        await Wire.Client(handler)
            .UpdateUserGroupAsync(
                id,
                new UpdateUserGroupRequest { Alias = "editors", Name = "Renamed" },
                CancellationToken.None
            );

        Assert.Contains(node.ToString(), handler.RequestBodies.Last(b => b is not null));
    }

    [Fact]
    public async Task DeleteUserGroupAsync_DeletesByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteUserGroupAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Contains($"user-group/{id}", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task DeleteUserGroupsAsync_DeletesCollectionWithIdsInBody()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteUserGroupsAsync([a, b], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.EndsWith("/user-group", handler.LastUri!.AbsolutePath);
        Assert.Contains(a.ToString(), handler.LastBody);
        Assert.Contains(b.ToString(), handler.LastBody);
    }

    [Fact]
    public async Task AddUsersToGroupAsync_PostsUsersToMembershipEndpoint()
    {
        var id = Guid.NewGuid();
        var user = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.AddUsersToGroupAsync(id, [user], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Contains($"user-group/{id}/users", handler.LastUri!.AbsoluteUri);
        Assert.Contains(user.ToString(), handler.LastBody);
    }

    [Fact]
    public async Task RemoveUsersFromGroupAsync_DeletesUsersFromMembershipEndpoint()
    {
        var id = Guid.NewGuid();
        var user = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.RemoveUsersFromGroupAsync(id, [user], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Contains($"user-group/{id}/users", handler.LastUri!.AbsoluteUri);
        Assert.Contains(user.ToString(), handler.LastBody);
    }

    [Fact]
    public async Task GetUserGroupsAsync_MapsItems()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""
            {"total":1,"items":[{"id":"{{id}}","alias":"editors","name":"Editors","sections":["Umb.Section.Content"],"isDeletable":true}]}
            """
        );

        var result = await client.GetUserGroupsAsync(0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/user-group", handler.LastUri!.AbsolutePath);
        var group = Assert.Single(result.Data!.Items);
        Assert.Equal("editors", group.Alias);
        Assert.Equal("Editors", group.Name);
        Assert.Contains("Umb.Section.Content", group.Sections);
        Assert.True(group.IsDeletable);
    }

    // ── User data ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserDataAsync_PostsFieldsAndEchoesKey()
    {
        var (client, handler) = ClientReturning("", HttpStatusCode.Created);
        var key = Guid.NewGuid();

        var result = await client.CreateUserDataAsync(
            new CreateUserDataRequest
            {
                Key = key,
                Group = "myGroup",
                Identifier = "theme",
                Value = "dark",
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/user-data", handler.LastUri!.AbsolutePath);
        Assert.Contains("theme", handler.LastBody);
        Assert.Equal(key, result.Data!.Key); // client-supplied key echoed
    }

    [Fact]
    public async Task UpdateUserDataAsync_PutsToCollectionWithKeyInBody()
    {
        var key = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.UpdateUserDataAsync(
            new UpdateUserDataRequest
            {
                Key = key,
                Group = "myGroup",
                Identifier = "theme",
                Value = "light",
            },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.EndsWith("/user-data", handler.LastUri!.AbsolutePath); // collection-level PUT
        Assert.Contains(key.ToString(), handler.LastBody);
        Assert.Contains("light", handler.LastBody);
    }

    [Fact]
    public async Task DeleteUserDataAsync_DeletesByKeyEndpoint()
    {
        var key = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteUserDataAsync(key, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Contains($"user-data/{key}", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetUserDataAsync_PassesFiltersAndMaps()
    {
        var key = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""
            {"total":1,"items":[{"key":"{{key}}","group":"myGroup","identifier":"theme","value":"dark"}]}
            """
        );

        var result = await client.GetUserDataAsync(
            "myGroup",
            "theme",
            0,
            100,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains("groups=myGroup", handler.LastUri!.AbsoluteUri);
        Assert.Contains("identifiers=theme", handler.LastUri!.AbsoluteUri);
        var entry = Assert.Single(result.Data!.Items);
        Assert.Equal("theme", entry.Identifier);
        Assert.Equal("dark", entry.Value);
    }

    [Fact]
    public async Task GetUserDataByIdAsync_EchoesRequestedKey()
    {
        var key = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            """{"group":"myGroup","identifier":"theme","value":"dark"}"""
        );

        var result = await client.GetUserDataByIdAsync(key, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"user-data/{key}", handler.LastUri!.AbsoluteUri);
        Assert.Equal(key, result.Data!.Key); // item body carries no key; the requested id is echoed
        Assert.Equal("dark", result.Data.Value);
    }

    [Fact]
    public async Task GetUserDataByIdAsync_EmptyNotFound_NamesTheMissingKey()
    {
        // #372: Umbraco's 404 here has no body; say what was not found instead of "unexpected".
        var key = Guid.NewGuid();
        var (client, _) = ClientReturning("", HttpStatusCode.NotFound);

        var result = await client.GetUserDataByIdAsync(key, CancellationToken.None);

        Assert.Equal(
            (404, $"No user data with key '{key}'."),
            (result.StatusCode, result.ErrorMessage)
        );
    }

    [Fact]
    public async Task GetUserDataByIdAsync_EmptyOk_IsNotFound()
    {
        var (client, _) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.GetUserDataByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteUserDataAsync_EmptyNotFound_NamesTheMissingKey()
    {
        var key = Guid.NewGuid();
        var (client, _) = ClientReturning("", HttpStatusCode.NotFound);

        var result = await client.DeleteUserDataAsync(key, CancellationToken.None);

        Assert.Equal($"No user data with key '{key}'.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateUserDataAsync_EmptyNotFound_NamesTheMissingKey()
    {
        var key = Guid.NewGuid();
        var (client, _) = ClientReturning("", HttpStatusCode.NotFound);

        var result = await client.UpdateUserDataAsync(
            new UpdateUserDataRequest
            {
                Key = key,
                Group = "g",
                Identifier = "i",
                Value = "v",
            },
            CancellationToken.None
        );

        Assert.Equal($"No user data with key '{key}'.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetUserDataByIdAsync_NotFoundWithProblemDetails_KeepsUmbracosMessage()
    {
        var (client, _) = ClientReturning(
            """{"title":"User data not found","status":404}""",
            HttpStatusCode.NotFound
        );

        var result = await client.GetUserDataByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.StartsWith("User data not found", result.ErrorMessage);
    }
}
