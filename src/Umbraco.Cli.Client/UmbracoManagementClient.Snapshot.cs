using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Raw-JSON passthrough surface of <see cref="UmbracoManagementClient"/>: full-fidelity
/// document-type/data-type/template access for the schema pipeline (#68 / ADR 0005) and document
/// access for the content pipeline (#100 / ADR 0006), plus the shared Kiota-adapter transport
/// helpers they are built on. Split into its own partial so the main client file is not the sole
/// home for this cohesive seam.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    // ── Schema (raw-JSON passthrough, #68 / ADR 0005) ──────────────────────────
    //
    // Full-fidelity reads/writes for the export/diff/apply pipeline. Unlike the typed
    // methods above, these carry the verbatim Management-API body as a JsonNode so nothing
    // is dropped (properties, config values, Razor). Reads reuse the hand-written GetAsync
    // helper (parsing the body straight into a JsonNode DOM); writes reuse PostAsync/PutAsync
    // and so flow through the intercepted HttpClient — meaning --dry-run and --readonly are
    // honoured on schema apply exactly as on any other write. The endpoints are the standard
    // by-id/collection routes (not the lossy tree/by-id mappers).

    /// <summary>Page size for walking a schema tree during enumeration.</summary>
    private const int TreePageSize = 100;

    /// <summary>A tree node normalised across the three kinds' generated tree-item models.</summary>
    /// <param name="Id">The node id.</param>
    /// <param name="IsFolder">Whether the node is an organisational folder (skipped from results, still recursed).</param>
    /// <param name="HasChildren">Whether the node has children to recurse into.</param>
    private readonly record struct TreeNode(Guid Id, bool IsFolder, bool HasChildren);

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetDocumentTypeIdsAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () => await WalkTreeAsync(FetchDocumentTypeTree, parent: null, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetDataTypeIdsAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () => await WalkTreeAsync(FetchDataTypeTree, parent: null, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetTemplateIdsAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () => await WalkTreeAsync(FetchTemplateTree, parent: null, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMediaTypeIdsAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () => await WalkTreeAsync(FetchMediaTypeTree, parent: null, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<Guid>>> GetMemberTypeIdsAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<Guid>>(
            ct,
            async () => await WalkTreeAsync(FetchMemberTypeTree, parent: null, ct)
        );

    /// <summary>
    /// Recursively enumerates a schema tree, returning the ids of every non-folder entity.
    /// Folders are not returned (they are not gettable as the entity) but are descended into;
    /// entities with children are also descended into (templates nest by inheritance). Each
    /// level is paged. The recursion depth is bounded by the tree's real nesting, which is
    /// shallow in practice.
    /// </summary>
    /// <param name="fetch">Fetches one page of tree nodes under a parent (null = root).</param>
    /// <param name="parent">The parent id to list children of, or null for the tree root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every non-folder entity id beneath <paramref name="parent"/>.</returns>
    private static async Task<List<Guid>> WalkTreeAsync(
        Func<Guid?, int, int, CancellationToken, Task<(List<TreeNode> Items, int Total)>> fetch,
        Guid? parent,
        CancellationToken ct
    )
    {
        var ids = new List<Guid>();
        var skip = 0;
        while (true)
        {
            var (items, total) = await fetch(parent, skip, TreePageSize, ct);
            foreach (var node in items)
            {
                if (!node.IsFolder)
                    ids.Add(node.Id);
                if (node.HasChildren)
                    ids.AddRange(await WalkTreeAsync(fetch, node.Id, ct));
            }

            skip += items.Count;
            // Stop on an empty page (defensive) or once the reported total is covered.
            if (items.Count == 0 || skip >= total)
                break;
        }
        return ids;
    }

    /// <summary>Fetches one page of the document-type tree (root when <paramref name="parent"/> is null).</summary>
    private async Task<(List<TreeNode>, int)> FetchDocumentTypeTree(
        Guid? parent,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        Gen.PagedDocumentTypeTreeItemResponseModel? paged;
        if (parent is null)
            paged = await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        else
            paged = await _api.Umbraco.Management.Api.V1.Tree.DocumentType.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );

        var items = (paged?.Items ?? [])
            .Select(i => new TreeNode(
                i.Id ?? Guid.Empty,
                i.IsFolder ?? false,
                i.HasChildren ?? false
            ))
            .ToList();
        return (items, (int)(paged?.Total ?? 0));
    }

    /// <summary>Fetches one page of the data-type tree (root when <paramref name="parent"/> is null).</summary>
    private async Task<(List<TreeNode>, int)> FetchDataTypeTree(
        Guid? parent,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        Gen.PagedDataTypeTreeItemResponseModel? paged;
        if (parent is null)
            paged = await _api.Umbraco.Management.Api.V1.Tree.DataType.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        else
            paged = await _api.Umbraco.Management.Api.V1.Tree.DataType.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );

        var items = (paged?.Items ?? [])
            .Select(i => new TreeNode(
                i.Id ?? Guid.Empty,
                i.IsFolder ?? false,
                i.HasChildren ?? false
            ))
            .ToList();
        return (items, (int)(paged?.Total ?? 0));
    }

    /// <summary>Fetches one page of the media-type tree (root when <paramref name="parent"/> is null).</summary>
    /// <param name="parent">The parent folder id, or null for the tree root.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's nodes and the total count.</returns>
    private async Task<(List<TreeNode>, int)> FetchMediaTypeTree(
        Guid? parent,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        Gen.PagedMediaTypeTreeItemResponseModel? paged;
        if (parent is null)
            paged = await _api.Umbraco.Management.Api.V1.Tree.MediaType.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        else
            paged = await _api.Umbraco.Management.Api.V1.Tree.MediaType.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );

        var items = (paged?.Items ?? [])
            .Select(i => new TreeNode(
                i.Id ?? Guid.Empty,
                i.IsFolder ?? false,
                i.HasChildren ?? false
            ))
            .ToList();
        return (items, (int)(paged?.Total ?? 0));
    }

    /// <summary>Fetches one page of the member-type tree (root when <paramref name="parent"/> is null).</summary>
    /// <param name="parent">The parent folder id, or null for the tree root.</param>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page's nodes and the total count.</returns>
    private async Task<(List<TreeNode>, int)> FetchMemberTypeTree(
        Guid? parent,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        Gen.PagedMemberTypeTreeItemResponseModel? paged;
        if (parent is null)
            paged = await _api.Umbraco.Management.Api.V1.Tree.MemberType.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        else
            paged = await _api.Umbraco.Management.Api.V1.Tree.MemberType.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );

        var items = (paged?.Items ?? [])
            .Select(i => new TreeNode(
                i.Id ?? Guid.Empty,
                i.IsFolder ?? false,
                i.HasChildren ?? false
            ))
            .ToList();
        return (items, (int)(paged?.Total ?? 0));
    }

    /// <summary>
    /// Fetches one page of the template tree. Templates are never foldered (they nest by
    /// inheritance), so every node is a real template (<c>IsFolder</c> is always false).
    /// </summary>
    private async Task<(List<TreeNode>, int)> FetchTemplateTree(
        Guid? parent,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        Gen.PagedNamedEntityTreeItemResponseModel? paged;
        if (parent is null)
            paged = await _api.Umbraco.Management.Api.V1.Tree.Template.Root.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );
        else
            paged = await _api.Umbraco.Management.Api.V1.Tree.Template.Children.GetAsync(
                c =>
                {
                    c.QueryParameters.ParentId = parent;
                    c.QueryParameters.Skip = skip;
                    c.QueryParameters.Take = take;
                },
                ct
            );

        var items = (paged?.Items ?? [])
            .Select(i => new TreeNode(i.Id ?? Guid.Empty, IsFolder: false, i.HasChildren ?? false))
            .ToList();
        return (items, (int)(paged?.Total ?? 0));
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetDocumentTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => GetRawJsonAsync($"umbraco/management/api/v1/document-type/{id}", ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetDataTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(ct, () => GetRawJsonAsync($"umbraco/management/api/v1/data-type/{id}", ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetTemplateRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => GetRawJsonAsync($"umbraco/management/api/v1/template/{id}", ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateDocumentTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.POST, "umbraco/management/api/v1/document-type", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateDocumentTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () =>
                SendRawJsonAsync(
                    Method.PUT,
                    $"umbraco/management/api/v1/document-type/{id}",
                    body,
                    ct
                )
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateDataTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.POST, "umbraco/management/api/v1/data-type", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateDataTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () =>
                SendRawJsonAsync(Method.PUT, $"umbraco/management/api/v1/data-type/{id}", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetMediaTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => GetRawJsonAsync($"umbraco/management/api/v1/media-type/{id}", ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetMemberTypeRawAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => GetRawJsonAsync($"umbraco/management/api/v1/member-type/{id}", ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateMediaTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.POST, "umbraco/management/api/v1/media-type", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateMediaTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () =>
                SendRawJsonAsync(Method.PUT, $"umbraco/management/api/v1/media-type/{id}", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateMemberTypeRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.POST, "umbraco/management/api/v1/member-type", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateMemberTypeRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () =>
                SendRawJsonAsync(
                    Method.PUT,
                    $"umbraco/management/api/v1/member-type/{id}",
                    body,
                    ct
                )
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateTemplateRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.POST, "umbraco/management/api/v1/template", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateTemplateRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.PUT, $"umbraco/management/api/v1/template/{id}", body, ct)
        );

    // ── Raw-JSON transport (schema passthrough) ────────────────────────────────
    //
    // The typed generated builders drop fields the schema pipeline must preserve
    // (properties, data-type config values, template Razor), so these endpoints are
    // driven straight off the Kiota request adapter with a verbatim JSON body/response
    // instead. Requests still flow through the same HttpClient the generated calls use,
    // so the bearer header, the --dry-run/--readonly mutation interceptor, and the
    // transport-failure guard all apply unchanged. Error bodies are mapped to the
    // generated ProblemDetails so GuardedApiAsync renders them like any other failure.

    /// <summary>Maps any 4xx/5xx response to the generated <see cref="Gen.ProblemDetails"/> so
    /// <see cref="GuardedApiAsync{T}"/> catches it and produces a readable failure.</summary>
    private static readonly Dictionary<string, ParsableFactory<IParsable>> RawErrorMapping = new()
    {
        { "4XX", Gen.ProblemDetails.CreateFromDiscriminatorValue },
        { "5XX", Gen.ProblemDetails.CreateFromDiscriminatorValue },
    };

    /// <summary>
    /// Issues a GET against <paramref name="path"/> and returns the response body as a verbatim
    /// <see cref="JsonNode"/> DOM (nothing is dropped or reshaped).
    /// </summary>
    /// <param name="path">The API path, relative to the host root (no leading slash).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed response body.</returns>
    /// <exception cref="ApiException">The response had no body to parse.</exception>
    private async Task<JsonNode> GetRawJsonAsync(string path, CancellationToken ct)
    {
        var requestInfo = new RequestInformation { HttpMethod = Method.GET, URI = RawUri(path) };
        requestInfo.Headers.TryAdd("Accept", "application/json");

        var stream = await _adapter.SendPrimitiveAsync<Stream>(requestInfo, RawErrorMapping, ct);
        if (stream is null)
            throw new ApiException("The Umbraco instance returned an empty body.")
            {
                ResponseStatusCode = 204,
            };
        return JsonNode.Parse(stream)
            ?? throw new ApiException("The Umbraco instance returned a null JSON body.");
    }

    /// <summary>
    /// Sends a state-changing request (<see cref="Method.POST"/>/<see cref="Method.PUT"/>) carrying
    /// <paramref name="body"/> verbatim as its JSON payload and expecting no response content.
    /// </summary>
    /// <param name="method">The HTTP method (POST to create, PUT to update).</param>
    /// <param name="path">The API path, relative to the host root (no leading slash).</param>
    /// <param name="body">The request body, sent byte-for-byte as serialized.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="Empty.Value"/> on success.</returns>
    private async Task<Empty> SendRawJsonAsync(
        Method method,
        string path,
        JsonNode body,
        CancellationToken ct
    )
    {
        var requestInfo = new RequestInformation { HttpMethod = method, URI = RawUri(path) };
        var payload = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body.ToJsonString()));
        requestInfo.SetStreamContent(payload, "application/json");

        await _adapter.SendNoContentAsync(requestInfo, RawErrorMapping, ct);
        return Empty.Value;
    }

    /// <summary>Builds the absolute request URI for a raw call from the adapter's base URL.</summary>
    /// <param name="path">The API path, relative to the host root (no leading slash).</param>
    /// <returns>The absolute URI to request.</returns>
    private Uri RawUri(string path) => new($"{_adapter.BaseUrl}/{path}");

    /// <summary>
    /// Raw-JSON scalar read-merge: GET the document at <paramref name="path"/> verbatim, overwrite
    /// only the supplied top-level string fields, and PUT the whole document back. A null value in
    /// <paramref name="patch"/> is skipped, leaving that field at its current value. This preserves
    /// every field the typed create/update models would drop (properties, containers, editor config)
    /// and is the reusable core behind the resource <c>update</c> verbs whose typed round-trip is
    /// lossy - see <see cref="UpdateMemberTypeAsync"/> and ADR 0005. The PUT flows through the
    /// intercepted <see cref="HttpClient"/>, so <c>--dry-run</c> previews it.
    /// </summary>
    /// <param name="path">The by-id resource path, relative to the host root (no leading slash).</param>
    /// <param name="patch">Top-level field name to new value; null values leave the field unchanged.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="Empty.Value"/> on success.</returns>
    private async Task<Empty> UpdateRawScalarsAsync(
        string path,
        IReadOnlyDictionary<string, string?> patch,
        CancellationToken ct
    )
    {
        var current = await GetRawJsonAsync(path, ct);
        foreach (var (key, value) in patch)
        {
            if (value is not null)
                current[key] = value;
        }
        await SendRawJsonAsync(Method.PUT, path, current, ct);
        return Empty.Value;
    }

    // ── Content snapshot (raw-JSON passthrough, #100 / ADR 0006) ────────────────
    //
    // Full-fidelity document reads/writes for the content export/diff/apply pipeline. Unlike the
    // typed content methods, these carry the verbatim document body so nothing is dropped. A
    // document's raw GET body does not include its parent (placement lives in the tree), so
    // GetDocumentTreeAsync captures id+parent from the tree walk and the exporter stores them
    // alongside each body; on apply the parent is written back into the create body.

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<ContentTreeNode>>> GetDocumentTreeAsync(
        Guid? root = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyList<ContentTreeNode>>(
            ct,
            async () =>
            {
                // With an explicit root, include the root itself as a portable top-level node
                // (parent null) so the exported subtree stands alone; then walk its descendants.
                if (root is null)
                    return await WalkDocumentTreeAsync(parent: null, ct);

                var nodes = new List<ContentTreeNode> { new(root.Value, Parent: null) };
                nodes.AddRange(await WalkDocumentTreeAsync(root, ct));
                return nodes;
            }
        );

    /// <summary>
    /// Recursively enumerates the document tree beneath <paramref name="parent"/> in pre-order,
    /// recording each document's id and its parent. Every level is paged; the recursion depth is
    /// bounded by the content tree's real nesting.
    /// </summary>
    /// <param name="parent">The parent whose children to list, or null for the content root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every document beneath <paramref name="parent"/> as id/parent pairs, pre-order.</returns>
    private async Task<List<ContentTreeNode>> WalkDocumentTreeAsync(
        Guid? parent,
        CancellationToken ct
    )
    {
        var nodes = new List<ContentTreeNode>();
        var skip = 0;
        while (true)
        {
            var paged = parent is null
                ? await _api.Umbraco.Management.Api.V1.Tree.Document.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                )
                : await _api.Umbraco.Management.Api.V1.Tree.Document.Children.GetAsync(
                    c =>
                    {
                        c.QueryParameters.ParentId = parent;
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = TreePageSize;
                    },
                    ct
                );

            var items = paged?.Items ?? [];
            foreach (var item in items)
            {
                var id = item.Id ?? Guid.Empty;
                // Pre-order: emit the node before descending, so parents precede their children.
                nodes.Add(new ContentTreeNode(id, parent));
                if (item.HasChildren ?? false)
                    nodes.AddRange(await WalkDocumentTreeAsync(id, ct));
            }

            skip += items.Count;
            if (items.Count == 0 || skip >= (int)(paged?.Total ?? 0))
                break;
        }
        return nodes;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetDocumentRawAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => GetRawJsonAsync($"umbraco/management/api/v1/document/{id}", ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> CreateDocumentRawAsync(
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.POST, "umbraco/management/api/v1/document", body, ct)
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateDocumentRawAsync(
        Guid id,
        JsonNode body,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            () => SendRawJsonAsync(Method.PUT, $"umbraco/management/api/v1/document/{id}", body, ct)
        );
}
