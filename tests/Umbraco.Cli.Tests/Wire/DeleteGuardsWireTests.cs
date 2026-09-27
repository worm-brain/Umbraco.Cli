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
    public async Task GetDocumentTypesUsingTemplateAsync_NamesTheTypesThatAllowOrDefaultToIt()
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

        var result = await Wire.Client(handler)
            .GetDocumentTypesUsingTemplateAsync(Id, CancellationToken.None);

        Assert.Equal(["Blog Post", "Home"], result.Data);
    }

    [Fact]
    public async Task GetDocumentTypesUsingTemplateAsync_TreeUnreadable_Fails()
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.Forbidden, "");

        var result = await Wire.Client(handler)
            .GetDocumentTypesUsingTemplateAsync(Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
