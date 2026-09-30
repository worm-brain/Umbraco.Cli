using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The reads behind the cascading-delete guards (#269): what the client asks Umbraco before a
/// template, member group or user group delete, and how it reads the answer.
/// </summary>
public class DeleteGuardsWireTests
{
    private static readonly Guid Id = Guid.Parse("26926926-9269-2692-6926-926926926926");

    [Fact]
    public async Task CountUsersInGroupAsync_FiltersUsersByTheGroup()
    {
        var handler = Wire.Routed(("/filter/user", """{ "total": 3, "items": [] }"""));

        await Wire.Client(handler).CountUsersInGroupAsync(Id, CancellationToken.None);

        Assert.Equal(
            Id.ToString(),
            handler.QueryOf(HttpMethod.Get, "/filter/user")["userGroupIds"]
        );
    }

    [Fact]
    public async Task CountUsersInGroupAsync_ReadsTheTotal()
    {
        var handler = Wire.Routed(("/filter/user", """{ "total": 3, "items": [] }"""));

        var result = await Wire.Client(handler).CountUsersInGroupAsync(Id, CancellationToken.None);

        Assert.Equal(3, result.Data);
    }

    [Fact]
    public async Task CountMembersInGroupAsync_FiltersMembersByTheGroupName()
    {
        // filter/member takes the group's NAME, so the group is read first (#184).
        var handler = Wire.Routed(
            ("/filter/member", """{ "total": 2, "items": [] }"""),
            ($"/member-group/{Id}", $$"""{ "id": "{{Id}}", "name": "VIP" }""")
        );

        await Wire.Client(handler).CountMembersInGroupAsync(Id, CancellationToken.None);

        Assert.Equal("VIP", handler.QueryOf(HttpMethod.Get, "/filter/member")["memberGroupName"]);
    }

    [Fact]
    public async Task CountMembersInGroupAsync_GroupMissing_Fails()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.NotFound, "");

        var result = await Wire.Client(handler)
            .CountMembersInGroupAsync(Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetTemplateUsageAsync_IndexesTheTypesThatAllowOrDefaultToEachTemplate()
    {
        var (blog, home, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var handler = Wire.Routed(
            (
                "/document-type/batch",
                $$"""
                { "total": 3, "items": [
                  { "id": "{{blog}}", "name": "Blog Post", "allowedTemplates": [ { "id": "{{Id}}" } ] },
                  { "id": "{{home}}", "name": "Home", "allowedTemplates": [], "defaultTemplate": { "id": "{{Id}}" } },
                  { "id": "{{other}}", "name": "Other", "allowedTemplates": [ { "id": "{{Guid.NewGuid()}}" } ] }
                ] }
                """
            ),
            (
                "/tree/document-type/root",
                $$"""
                { "total": 3, "items": [
                  { "id": "{{blog}}", "isFolder": false, "hasChildren": false },
                  { "id": "{{home}}", "isFolder": false, "hasChildren": false },
                  { "id": "{{other}}", "isFolder": false, "hasChildren": false }
                ] }
                """
            )
        );

        var result = await Wire.Client(handler).GetTemplateUsageAsync(CancellationToken.None);

        Assert.Equal(["Blog Post", "Home"], result.Data![Id].Select(u => u.Name));
    }

    // ── Umbraco 17.0-17.2: no /document-type/batch (#432) ───────────────────

    /// <summary>
    /// A site without batch endpoints holding three document types: Blog Post allows the template,
    /// Home defaults to it, Other uses another. Each by-id read answers from
    /// <paramref name="read"/>, which can fail one to show what the guard does.
    /// </summary>
    /// <param name="read">The status and body for a type's by-id read.</param>
    /// <returns>The handler and the three ids.</returns>
    private static (RoutingHandler Handler, Guid Blog, Guid Home, Guid Other) SiteWithoutBatch(
        Func<Guid, (Guid Blog, Guid Home, Guid Other), (HttpStatusCode, string)>? read = null
    )
    {
        var ids = (Blog: Guid.NewGuid(), Home: Guid.NewGuid(), Other: Guid.NewGuid());
        var bodies = new Dictionary<Guid, string>
        {
            [ids.Blog] =
                $$"""{ "id": "{{ids.Blog}}", "name": "Blog Post", "allowedTemplates": [ { "id": "{{Id}}" } ] }""",
            [ids.Home] =
                $$"""{ "id": "{{ids.Home}}", "name": "Home", "allowedTemplates": [], "defaultTemplate": { "id": "{{Id}}" } }""",
            [ids.Other] =
                $$"""{ "id": "{{ids.Other}}", "name": "Other", "allowedTemplates": [ { "id": "{{Guid.NewGuid()}}" } ] }""",
        };
        var handler = new RoutingHandler().When(
            r => r.RequestUri!.AbsolutePath.EndsWith("/batch"),
            HttpStatusCode.NotFound,
            ""
        );
        foreach (var (id, body) in bodies)
        {
            var (status, answer) = read?.Invoke(id, ids) ?? (HttpStatusCode.OK, body);
            handler.When(
                r => r.RequestUri!.AbsolutePath.EndsWith($"/document-type/{id}"),
                status,
                answer
            );
        }
        handler.When(
            r => r.RequestUri!.AbsolutePath.EndsWith("/tree/document-type/root"),
            HttpStatusCode.OK,
            $$"""
            { "total": 3, "items": [
              { "id": "{{ids.Blog}}", "isFolder": false, "hasChildren": false },
              { "id": "{{ids.Home}}", "isFolder": false, "hasChildren": false },
              { "id": "{{ids.Other}}", "isFolder": false, "hasChildren": false }
            ] }
            """
        );
        return (handler, ids.Blog, ids.Home, ids.Other);
    }

    [Fact]
    public async Task GetTemplateUsageAsync_NoBatchEndpoint_ReadsEachTypeAndIndexesThem()
    {
        // Arrange
        var (handler, _, _, _) = SiteWithoutBatch();

        // Act
        var result = await Wire.Client(handler).GetTemplateUsageAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["Blog Post", "Home"], result.Data![Id].Select(u => u.Name).Order());
    }

    [Fact]
    public async Task GetTemplateUsageAsync_NoBatchEndpoint_AReadFails_FailsWithThatStatus()
    {
        // Arrange: Home's read fails, so whether it uses the template is unknown.
        var (handler, _, _, _) = SiteWithoutBatch(
            (id, ids) =>
                id == ids.Home
                    ? (HttpStatusCode.InternalServerError, """{"title":"Boom"}""")
                    : (
                        HttpStatusCode.OK,
                        $$"""{ "id": "{{id}}", "name": "T", "allowedTemplates": [] }"""
                    )
        );

        // Act
        var result = await Wire.Client(handler).GetTemplateUsageAsync(CancellationToken.None);

        // Assert: fails closed, rather than answering "unused" and letting the delete through.
        Assert.Equal((false, 500), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task GetTemplateUsageAsync_NoBatchEndpoint_TypeDeletedSinceTheTreeWalk_IsSkipped()
    {
        // Arrange: Other was deleted between the tree walk and its read; the batch would leave
        // it out the same way.
        var (handler, _, _, _) = SiteWithoutBatch(
            (id, ids) =>
                id == ids.Other
                    ? (HttpStatusCode.NotFound, "")
                    : (
                        HttpStatusCode.OK,
                        id == ids.Blog
                            ? $$"""{ "id": "{{id}}", "name": "Blog Post", "allowedTemplates": [ { "id": "{{Id}}" } ] }"""
                            : $$"""{ "id": "{{id}}", "name": "Home", "allowedTemplates": [] }"""
                    )
        );

        // Act
        var result = await Wire.Client(handler).GetTemplateUsageAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Blog Post", Assert.Single(result.Data![Id]).Name);
    }

    [Fact]
    public async Task GetTemplateUsageAsync_TreeUnreadable_Fails()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.Forbidden, "");

        var result = await Wire.Client(handler).GetTemplateUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
