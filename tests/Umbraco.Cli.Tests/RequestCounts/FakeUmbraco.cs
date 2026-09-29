using System.Net;
using System.Text.Json.Nodes;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Routes that make a <see cref="RoutingHandler"/> answer like a small Umbraco instance, for the
/// request-count tests (#408): trees served from a <see cref="FakeTree"/>, by-id reads answered
/// with a body built from the id, and the client-credentials token endpoint. Routes are matched in
/// the order registered, so register the specific ones before <see cref="ElseEmpty"/>.
/// </summary>
internal static class FakeUmbraco
{
    /// <summary>The Management API path prefix.</summary>
    private const string Api = "/umbraco/management/api/v1";

    /// <summary>The path of the OAuth token endpoint.</summary>
    public const string TokenPath = $"{Api}/security/back-office/token";

    /// <summary>The document type every by-id <see cref="Document"/> body names.</summary>
    private static readonly Guid SharedDocumentType = new("5e1f0a3c-0000-4000-8000-000000000001");

    /// <summary>Whether a recorded request is a client-credentials token exchange.</summary>
    /// <param name="request">The recorded request.</param>
    /// <returns>True for a POST to <see cref="TokenPath"/>.</returns>
    public static bool IsTokenRequest(Recorded request) =>
        request.Method == HttpMethod.Post
        && request.Uri.AbsolutePath.Equals(TokenPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An instance holding <paramref name="documents"/>: the document tree, each document's by-id
    /// body, each document type's by-id body, the token endpoint, and an empty 200 for anything
    /// else (the writes).
    /// </summary>
    /// <param name="documents">The content tree.</param>
    /// <returns>The handler.</returns>
    public static RoutingHandler ContentSite(FakeTree documents) =>
        new RoutingHandler()
            .ServeToken()
            .ServeTree("document", documents)
            .ServeById("document-type", ContentType)
            .ServeById("document", Document)
            .ElseEmpty();

    /// <summary>Serves <c>tree/{kind}/root</c> and <c>tree/{kind}/children</c> from <paramref name="tree"/>.</summary>
    /// <param name="handler">The handler to add the route to.</param>
    /// <param name="kind">The tree segment, e.g. <c>document</c> or <c>data-type</c>.</param>
    /// <param name="tree">The nodes to serve.</param>
    /// <returns>The handler, for chaining.</returns>
    public static RoutingHandler ServeTree(
        this RoutingHandler handler,
        string kind,
        FakeTree tree
    ) =>
        handler.When(
            r =>
                r.Method == HttpMethod.Get
                && (
                    r.RequestUri!.AbsolutePath.EndsWith($"/tree/{kind}/root")
                    || r.RequestUri.AbsolutePath.EndsWith($"/tree/{kind}/children")
                ),
            HttpStatusCode.OK,
            tree.Page
        );

    /// <summary>
    /// Answers <c>GET {resource}/{id}</c> with <paramref name="body"/>'s JSON for that id. Only the
    /// exact by-id path matches, so <c>{resource}/{id}/something</c> falls through.
    /// </summary>
    /// <param name="handler">The handler to add the route to.</param>
    /// <param name="resource">The resource segment(s), e.g. <c>document</c> or <c>document-type</c>.</param>
    /// <param name="body">Builds the body for an id.</param>
    /// <returns>The handler, for chaining.</returns>
    public static RoutingHandler ServeById(
        this RoutingHandler handler,
        string resource,
        Func<Guid, JsonObject> body
    ) =>
        handler.When(
            r => r.Method == HttpMethod.Get && ByIdTarget(r, resource) is not null,
            HttpStatusCode.OK,
            r => body(ByIdTarget(r, resource)!.Value).ToJsonString()
        );

    /// <summary>
    /// Answers a <c>GET</c> to a path ending in <paramref name="suffix"/> with a <c>{total, items}</c>
    /// page of <paramref name="items"/>, paged by the request's <c>skip</c> and <c>take</c>. It is
    /// served as a one-level <see cref="FakeTree"/>, so each item also gets a fresh <c>id</c>, a
    /// null <c>parent</c> and <c>hasChildren: false</c>.
    /// </summary>
    /// <param name="handler">The handler to add the route to.</param>
    /// <param name="suffix">The collection path suffix, e.g. <c>/user-group</c>.</param>
    /// <param name="items">Every item in the collection.</param>
    /// <returns>The handler, for chaining.</returns>
    public static RoutingHandler ServeCollection(
        this RoutingHandler handler,
        string suffix,
        IReadOnlyList<JsonObject> items
    )
    {
        var collection = new FakeTree();
        foreach (var item in items)
            collection.Add(Guid.NewGuid(), null, item);
        return handler.When(
            r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith(suffix),
            HttpStatusCode.OK,
            collection.Page
        );
    }

    /// <summary>Issues a 299-second token (Umbraco's lifetime) for every client-credentials exchange.</summary>
    /// <param name="handler">The handler to add the route to.</param>
    /// <returns>The handler, for chaining.</returns>
    public static RoutingHandler ServeToken(this RoutingHandler handler) =>
        handler.When(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.Equals(TokenPath, StringComparison.OrdinalIgnoreCase),
            HttpStatusCode.OK,
            """{"access_token":"issued","expires_in":299,"token_type":"Bearer"}"""
        );

    /// <summary>
    /// Answers everything not routed above with 200 and an empty body: what Umbraco returns for a
    /// successful write (publish, delete).
    /// </summary>
    /// <param name="handler">The handler to add the route to.</param>
    /// <returns>The handler.</returns>
    public static RoutingHandler ElseEmpty(this RoutingHandler handler) =>
        handler.When(_ => true, HttpStatusCode.OK, "");

    /// <summary>A document tree item: one invariant, published variant with a name.</summary>
    /// <param name="name">The document name.</param>
    /// <param name="documentType">The document type id.</param>
    /// <returns>The tree-item fields.</returns>
    public static JsonObject DocumentItem(string name, Guid documentType) =>
        new()
        {
            ["documentType"] = new JsonObject { ["id"] = documentType },
            ["variants"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = name,
                    ["culture"] = null,
                    ["state"] = "Published",
                }
            ),
        };

    /// <summary>A media tree item: one variant with a name.</summary>
    /// <param name="name">The media item name.</param>
    /// <param name="mediaType">The media type id.</param>
    /// <returns>The tree-item fields.</returns>
    public static JsonObject MediaItem(string name, Guid mediaType) =>
        new()
        {
            ["mediaType"] = new JsonObject { ["id"] = mediaType },
            ["variants"] = new JsonArray(new JsonObject { ["name"] = name, ["culture"] = null }),
        };

    /// <summary>A tree item that has only a name, as dictionary items and schema types do.</summary>
    /// <param name="name">The item name.</param>
    /// <param name="isFolder">Whether the item is an organisational folder.</param>
    /// <returns>The tree-item fields.</returns>
    public static JsonObject NamedItem(string name, bool isFolder = false) =>
        new() { ["name"] = name, ["isFolder"] = isFolder };

    /// <summary>
    /// A document's by-id body: one invariant, published variant and no values. The document type
    /// is the same for every document, since no count here depends on it.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <returns>The body.</returns>
    public static JsonObject Document(Guid id) =>
        new()
        {
            ["id"] = id,
            ["documentType"] = new JsonObject { ["id"] = SharedDocumentType },
            ["isTrashed"] = false,
            ["values"] = new JsonArray(),
            ["variants"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = $"Doc {id:N}",
                    ["culture"] = null,
                    ["segment"] = null,
                    ["state"] = "Published",
                    ["createDate"] = "2026-09-01T00:00:00+00:00",
                    ["updateDate"] = "2026-09-01T00:00:00+00:00",
                }
            ),
        };

    /// <summary>
    /// A schema entity's by-id body (a document, media or member type, a data type, template or
    /// group): its id, alias and name, which is all the reads here look at.
    /// </summary>
    /// <param name="id">The entity id.</param>
    /// <returns>The body.</returns>
    public static JsonObject ContentType(Guid id) =>
        new()
        {
            ["id"] = id,
            ["alias"] = $"type{id:N}",
            ["name"] = $"Type {id:N}",
        };

    /// <summary>The id a by-id request targets, when its path is exactly <c>{resource}/{guid}</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="resource">The resource segment(s).</param>
    /// <returns>The id, or null when the path is anything else.</returns>
    private static Guid? ByIdTarget(HttpRequestMessage request, string resource)
    {
        var prefix = $"{Api}/{resource}/";
        var path = request.RequestUri!.AbsolutePath;
        return
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(path[prefix.Length..], out var id)
            ? id
            : null;
    }
}
