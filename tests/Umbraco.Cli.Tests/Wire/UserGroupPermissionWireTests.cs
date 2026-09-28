using System.Net;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Granular per-document user-group permissions on the wire (#111). The <c>$type</c> discriminator
/// is what tells Umbraco which permission kind an entry is, so it is pinned; and an update must
/// never drop the granular kinds the CLI does not model.
/// </summary>
public class UserGroupPermissionWireTests
{
    private static readonly Guid GroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Blog = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid News = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A group with a document permission on Blog and a property-value permission.</summary>
    private static readonly string Existing = $$"""
        { "id": "{{GroupId}}", "alias": "editors", "name": "Editors", "sections": [], "languages": [],
          "fallbackPermissions": [], "hasAccessToAllLanguages": false,
          "documentRootAccess": false, "mediaRootAccess": false,
          "permissions": [
            { "$type": "DocumentPermissionPresentationModel",
              "document": { "id": "{{Blog}}" }, "verbs": ["Umb.Document.Read"] },
            { "$type": "DocumentPropertyValuePermissionPresentationModel",
              "documentType": { "id": "{{News}}" }, "propertyType": { "id": "{{News}}" },
              "verbs": ["Umb.Document.PropertyValue.Read"] } ] }
        """;

    private static JsonArray PermissionsSent(
        RoutingHandler handler,
        HttpMethod method,
        string path
    ) => handler.BodyOf(method, path)["permissions"]!.AsArray();

    [Fact]
    public async Task CreateUserGroupAsync_DocumentPermission_IsSentWithItsTypeDiscriminator()
    {
        var handler = Wire.Blank();

        await Wire.Client(handler)
            .CreateUserGroupAsync(
                new CreateUserGroupRequest
                {
                    Alias = "editors",
                    Name = "Editors",
                    DocumentPermissions =
                    [
                        new DocumentPermission { Document = Blog, Verbs = ["Umb.Document.Read"] },
                    ],
                },
                CancellationToken.None
            );

        var sent = Assert.Single(PermissionsSent(handler, HttpMethod.Post, "/user-group"))!;
        Assert.Equal(
            ("DocumentPermissionPresentationModel", Blog.ToString(), "Umb.Document.Read"),
            (
                sent["$type"]!.GetValue<string>(),
                sent["document"]!["id"]!.GetValue<string>(),
                sent["verbs"]![0]!.GetValue<string>()
            )
        );
    }

    [Fact]
    public async Task UpdateUserGroupAsync_NewDocumentPermissions_ReplaceTheDocumentOnesAndKeepOtherKinds()
    {
        var handler = Wire.Existing(Existing);

        await Wire.Client(handler)
            .UpdateUserGroupAsync(
                GroupId,
                new UpdateUserGroupRequest
                {
                    Alias = "editors",
                    Name = "Editors",
                    DocumentPermissions =
                    [
                        new DocumentPermission { Document = News, Verbs = ["Umb.Document.Update"] },
                    ],
                },
                CancellationToken.None
            );

        var types = PermissionsSent(handler, HttpMethod.Put, $"/user-group/{GroupId}")
            .Select(p => $"{p!["$type"]}:{p["document"]?["id"]}");
        Assert.Equal(
            [
                "DocumentPropertyValuePermissionPresentationModel:",
                $"DocumentPermissionPresentationModel:{News}",
            ],
            types
        );
    }

    [Fact]
    public async Task UpdateUserGroupAsync_NoDocumentPermissionsGiven_KeepsTheCurrentOnes()
    {
        var handler = Wire.Existing(Existing);

        await Wire.Client(handler)
            .UpdateUserGroupAsync(
                GroupId,
                new UpdateUserGroupRequest { Alias = "editors", Name = "Renamed" },
                CancellationToken.None
            );

        Assert.Equal(2, PermissionsSent(handler, HttpMethod.Put, $"/user-group/{GroupId}").Count);
    }

    [Fact]
    public async Task GetUserGroupByIdAsync_MapsTheDocumentPermissions()
    {
        var handler = Wire.Returning(Existing);

        var result = await Wire.Client(handler)
            .GetUserGroupByIdAsync(GroupId, CancellationToken.None);

        var permission = Assert.Single(result.Data!.DocumentPermissions);
        Assert.Equal(
            (Blog, "Umb.Document.Read"),
            (permission.Document, Assert.Single(permission.Verbs))
        );
    }
}
