using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for the reads migrated to the generated client in #79 (document-type/data-type by-id,
/// user list + by-id). These previously used the hand-written path and had no direct client
/// coverage; the tests drive the real <see cref="UmbracoManagementClient"/> through a canned
/// response so the generated-model -> DTO mapping (and thus the JSON output contract) is
/// asserted. Dictionary and webhook reads are already covered elsewhere.
/// </summary>
public class OverlookedReadsClientTests
{
    /// <summary>Builds a client that answers every request with <paramref name="json"/>.</summary>
    /// <param name="json">The canned JSON body.</param>
    /// <returns>A client bound to a single-body routing handler.</returns>
    private static UmbracoManagementClient Client(string json)
    {
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.OK, json);
        return new UmbracoManagementClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") }
        );
    }

    /// <summary>The document-type by-id read surfaces the alias, description and root flag.</summary>
    [Fact]
    public async Task GetDocumentTypeByIdAsync_MapsAliasAndFlags()
    {
        var id = Guid.NewGuid();
        var client = Client(
            $$"""
            {"id":"{{id}}","alias":"textPage","name":"Text Page","isElement":false,"allowedAsRoot":true,"description":"A page"}
            """
        );

        var result = await client.GetDocumentTypeByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("textPage", result.Data!.Alias);
        Assert.Equal("Text Page", result.Data.Name);
        Assert.True(result.Data.AllowedAsRoot);
    }

    /// <summary>The data-type by-id read surfaces both editor aliases.</summary>
    [Fact]
    public async Task GetDataTypeByIdAsync_MapsEditorAliases()
    {
        var id = Guid.NewGuid();
        var client = Client(
            $$"""
            {"id":"{{id}}","name":"Textstring","editorAlias":"Umbraco.TextBox","editorUiAlias":"Umb.PropertyEditorUi.TextBox"}
            """
        );

        var result = await client.GetDataTypeByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Umbraco.TextBox", result.Data!.EditorAlias);
        Assert.Equal("Umb.PropertyEditorUi.TextBox", result.Data.EditorUiAlias);
    }

    /// <summary>The user list maps items, flattening the user-state enum to its name.</summary>
    [Fact]
    public async Task GetUsersAsync_MapsItemsAndState()
    {
        var id = Guid.NewGuid();
        var client = Client(
            $$"""
            {"total":1,"items":[{"id":"{{id}}","name":"Admin","email":"a@example.com","userName":"admin","state":"Active"}]}
            """
        );

        var result = await client.GetUsersAsync(0, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(result.Data!.Items);
        Assert.Equal("Admin", user.Name);
        Assert.Equal("a@example.com", user.Email);
        Assert.Equal("Active", user.State);
    }

    /// <summary>The user by-id read maps the single user.</summary>
    [Fact]
    public async Task GetUserByIdAsync_MapsUser()
    {
        var id = Guid.NewGuid();
        var client = Client(
            $$"""
            {"id":"{{id}}","name":"Editor","email":"e@example.com","userName":"editor","state":"Inactive"}
            """
        );

        var result = await client.GetUserByIdAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Editor", result.Data!.Name);
        Assert.Equal("editor", result.Data.UserName);
    }
}
