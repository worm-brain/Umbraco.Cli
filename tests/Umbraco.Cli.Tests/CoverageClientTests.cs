using System.Net;
using System.Text;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the coverage client methods (#107): member-group CRUD hits the right endpoints and
/// round-trips id/name, and the read-only tag/culture lists map the paged bodies (culture's isoCode
/// comes from the generated model's <c>name</c>). Uses a recording stub over the real client.
/// </summary>
public class CoverageClientTests
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

    [Fact]
    public async Task CreateMemberGroupAsync_PostsNameAndEchoesId()
    {
        // The create is followed by a read-back GET (#391), so the POST is the first request.
        var handler = new RoutingHandler().When(_ => true, HttpStatusCode.Created, "");
        var id = Guid.NewGuid();

        var result = await Wire.Client(handler)
            .CreateMemberGroupAsync(
                new CreateMemberGroupRequest { Id = id, Name = "Editors" },
                CancellationToken.None
            );

        var post = handler.Recordings[0];
        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.EndsWith("/member-group", post.Uri.AbsolutePath);
        Assert.Contains("Editors", post.Body);
        Assert.Equal(id, result.Data!.Id); // client-supplied id echoed
    }

    [Fact]
    public async Task CreateMemberGroupAsync_ReadsTheGroupBack_SoTheResultMatchesGet()
    {
        // #391: the result is the saved group, not the request echoed back.
        var id = Guid.NewGuid();
        var handler = new RoutingHandler()
            .When(
                r => r.Method == HttpMethod.Get,
                HttpStatusCode.OK,
                $$"""{ "id": "{{id}}", "name": "Saved Name" }"""
            )
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateMemberGroupAsync(
                new CreateMemberGroupRequest { Id = id, Name = "Editors" },
                CancellationToken.None
            );

        Assert.Equal("Saved Name", result.Data!.Name);
    }

    [Fact]
    public async Task CreateMemberGroupAsync_ReadBackFails_StillSucceedsWithTheRequest()
    {
        // The group was created; a failed read-back must not turn that into an error.
        var handler = new RoutingHandler()
            .When(r => r.Method == HttpMethod.Get, HttpStatusCode.InternalServerError, "")
            .When(_ => true, HttpStatusCode.Created, "");

        var result = await Wire.Client(handler)
            .CreateMemberGroupAsync(
                new CreateMemberGroupRequest { Name = "Editors" },
                CancellationToken.None
            );

        Assert.Equal((true, "Editors"), (result.IsSuccess, result.Data!.Name));
    }

    [Fact]
    public async Task UpdateMemberGroupAsync_PutsNameToByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.UpdateMemberGroupAsync(
            id,
            new UpdateMemberGroupRequest { Name = "Renamed" },
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Contains($"member-group/{id}", handler.LastUri!.AbsoluteUri);
        Assert.Contains("Renamed", handler.LastBody);
    }

    [Fact]
    public async Task DeleteMemberGroupAsync_DeletesByIdEndpoint()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning("", HttpStatusCode.OK);

        var result = await client.DeleteMemberGroupAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Contains($"member-group/{id}", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetMemberGroupsAsync_MapsTreeItems()
    {
        var id = Guid.NewGuid();
        var (client, handler) = ClientReturning(
            $$"""{"total":1,"items":[{"id":"{{id}}","name":"Editors"}]}"""
        );

        var result = await client.GetMemberGroupsAsync(0, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("/tree/member-group/root", handler.LastUri!.AbsoluteUri);
        var group = Assert.Single(result.Data!.Items);
        Assert.Equal("Editors", group.Name);
    }

    [Fact]
    public async Task GetTagsAsync_PassesFiltersAndMaps()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"id":"3f7a8b2e-1234-5678-abcd-ef0123456789","text":"news","group":"default","nodeCount":5}]}"""
        );

        var result = await client.GetTagsAsync("default", "en-US", 0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tagGroup=default", handler.LastUri!.AbsoluteUri);
        Assert.Contains("culture=en-US", handler.LastUri!.AbsoluteUri);
        var tag = Assert.Single(result.Data!.Items);
        Assert.Equal("news", tag.Text);
        Assert.Equal(5, tag.NodeCount);
    }

    [Fact]
    public async Task GetCulturesAsync_MapsIsoCodeFromName()
    {
        var (client, handler) = ClientReturning(
            """{"total":1,"items":[{"name":"en-US","englishName":"English (United States)"}]}"""
        );

        var result = await client.GetCulturesAsync(0, 100, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.EndsWith("/culture", handler.LastUri!.AbsolutePath);
        var culture = Assert.Single(result.Data!.Items);
        Assert.Equal("en-US", culture.IsoCode);
        Assert.Equal("English (United States)", culture.EnglishName);
    }
}
