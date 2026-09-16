using System.Net;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of <see cref="UmbracoManagementClient.UpdateMemberTypeAsync"/> (#56): the update is
/// a raw-JSON read-merge, so it must GET the current member type, change only the supplied scalar
/// fields, and PUT the whole document back with the type's properties (which the CLI never
/// exposes) preserved. Uses <see cref="RoutingHandler"/> to answer the GET and capture the PUT.
/// </summary>
public class MemberTypeUpdateClientTests
{
    private static UmbracoManagementClient Client(RoutingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") });

    /// <summary>A member-type GET body carrying a property the typed update model would drop.</summary>
    private static string MemberTypeJson(Guid id) =>
        $$"""
            {
              "id": "{{id}}",
              "name": "Author",
              "alias": "author",
              "description": "Original",
              "icon": "icon-user",
              "properties": [ { "alias": "bio", "name": "Bio" } ],
              "containers": [],
              "compositions": []
            }
            """;

    [Fact]
    public async Task UpdateMemberTypeAsync_PatchesSuppliedFields_AndPreservesProperties()
    {
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.OK, MemberTypeJson(id))
            .When(r => r.Method == HttpMethod.Put, HttpStatusCode.OK, "");

        var result = await Client(handler)
            .UpdateMemberTypeAsync(
                id,
                new UpdateMemberTypeRequest { Name = "Blogger", Icon = "icon-edit" },
                CancellationToken.None
            );

        Assert.True(result.IsSuccess);
        // It read then wrote the same by-id endpoint (GET to fetch, PUT to replace).
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains($"member-type/{id}", handler.Requests[0].AbsoluteUri);
        Assert.Contains($"member-type/{id}", handler.Requests[1].AbsoluteUri);

        var put = handler.BodyForFirst(r => r.Method == HttpMethod.Put);
        // Supplied fields changed...
        Assert.Contains("\"name\":\"Blogger\"", put.Replace(" ", ""));
        Assert.Contains("\"icon\":\"icon-edit\"", put.Replace(" ", ""));
        // ...the omitted field kept its current value...
        Assert.Contains("\"alias\":\"author\"", put.Replace(" ", ""));
        // ...and the unexposed property survived the round-trip (the whole point of the read-merge).
        Assert.Contains("\"bio\"", put);
    }

    [Fact]
    public async Task UpdateMemberTypeAsync_MapsGetFailureToFailure()
    {
        var handler = new RoutingHandler().When(
            _ => true,
            HttpStatusCode.NotFound,
            """{"title":"Not Found"}"""
        );

        var result = await Client(handler)
            .UpdateMemberTypeAsync(
                Guid.NewGuid(),
                new UpdateMemberTypeRequest { Name = "X" },
                CancellationToken.None
            );

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        // A failed read must short-circuit: only the GET was attempted, never the PUT.
        Assert.Single(handler.Requests);
    }
}
