using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Document-blueprint resource on <see cref="UmbracoManagementClient"/> (issue #113). Kept in its
/// own partial so the main client file stays focused. The create/update body mirrors the document
/// (content) body - the same <see cref="Gen.DocumentValueModel"/>/<see cref="Gen.DocumentVariantRequestModel"/>
/// shapes and the <see cref="UntypedNodeFactory"/> value converter are reused. get/scaffold/create
/// return raw JSON via the shared <c>GetRawJsonAsync</c> seam so no property-value fidelity is lost.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>The Management API path prefix for the blueprint resource (used by the raw seam).</summary>
    private const string BlueprintPath = "umbraco/management/api/v1/document-blueprint";

    // ── Listing (tree) ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<
        UmbracoResponse<PagedResponse<DocumentBlueprintTreeItem>>
    > GetDocumentBlueprintsAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // No collection GET exists on /document-blueprint: the tree root/children endpoints
                // are the only listing surface. Root when no parent, children under a folder.
                var tree = _api.Umbraco.Management.Api.V1.Tree.DocumentBlueprint;
                var paged = parentId is { } pid
                    ? await tree.Children.GetAsync(
                        c =>
                        {
                            c.QueryParameters.ParentId = pid;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                    : await tree.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<DocumentBlueprintTreeItem>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new DocumentBlueprintTreeItem
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            DocumentType = i.DocumentType?.Id is { } dtId
                                ? new ContentTypeReference { Id = dtId }
                                : null,
                            IsFolder = i.IsFolder ?? false,
                            HasChildren = i.HasChildren ?? false,
                            Parent = i.Parent?.Id is { } pId
                                ? new ContentParentReference { Id = pId }
                                : null,
                        })
                        .ToList(),
                };
            }
        );

    // ── Read (raw, full fidelity) ────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> GetDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => GetRawJsonAsync($"{BlueprintPath}/{id}", ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> ScaffoldDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => GetRawJsonAsync($"{BlueprintPath}/{id}/scaffold", ct));

    // ── Create ───────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> CreateDocumentBlueprintAsync(
        CreateDocumentBlueprintRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // A GUID document-type reference is used directly; an alias is resolved to its id.
                var reference =
                    request.DocumentType.Id != Guid.Empty
                        ? request.DocumentType.Id.ToString()
                        : request.DocumentType.Alias;
                var documentTypeId = await IdOfAsync(EntityKind.DocumentType, reference, ct);

                var id = request.Id ?? Guid.NewGuid();

                // As for content create (#228): a culture-less variant on a type that varies by
                // culture gets the default language.
                var variants = MapVariants(request.Variants);
                await DefaultVariantCulturesAsync(variants, documentTypeId, ct);

                var body = new Gen.CreateDocumentBlueprintRequestModel
                {
                    Id = id,
                    DocumentType = new Gen.ReferenceByIdModel { Id = documentTypeId },
                    Parent = request.Parent is { } p
                        ? new Gen.ReferenceByIdModel { Id = p.Id }
                        : null,
                    Variants = variants,
                    Values = MapValues(request.Values),
                };
                await _api.Umbraco.Management.Api.V1.DocumentBlueprint.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return await HydrateBlueprintAsync(id, ct);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> CreateDocumentBlueprintFromDocumentAsync(
        CreateBlueprintFromDocumentRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var id = request.Id ?? Guid.NewGuid();
                var body = new Gen.CreateDocumentBlueprintFromDocumentRequestModel
                {
                    Id = id,
                    Document = new Gen.ReferenceByIdModel { Id = request.Document },
                    Name = request.Name,
                    Parent = request.Parent is { } p
                        ? new Gen.ReferenceByIdModel { Id = p.Id }
                        : null,
                };
                await _api.Umbraco.Management.Api.V1.DocumentBlueprint.FromDocument.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return await HydrateBlueprintAsync(id, ct);
            }
        );

    // ── Mutate ───────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// The PUT replaces the whole body, so sending only the supplied values dropped every other
    /// field (#242: featuredImage and publishDate vanished). Like <c>content update</c>, the
    /// blueprint is read and the request overlaid on it with <see cref="DocumentUpdateBody.Merge"/>
    /// unless <paramref name="replace"/> is set.
    /// </remarks>
    public Task<UmbracoResponse<Empty>> UpdateDocumentBlueprintAsync(
        Guid id,
        UpdateDocumentBlueprintRequest request,
        bool replace = false,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var path = $"umbraco/management/api/v1/document-blueprint/{id}";
                var blueprint =
                    await GetRawJsonAsync(path, ct) as JsonObject
                    ?? throw new ApiException("The blueprint body was not a JSON object.");
                // A culture-less rename on a variant blueprint means the default language
                // (#228); the merge's guard still refuses it when that language is missing.
                var variants = await DefaultExistingVariantCulturesAsync(
                    blueprint["variants"] as JsonArray,
                    request.Variants,
                    ct
                );
                DocumentUpdateBody.Merge(blueprint, request.Values, variants, replace);
                await SendRawJsonAsync(Method.PUT, path, blueprint, ct);
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DocumentBlueprint[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> MoveDocumentBlueprintAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DocumentBlueprint[id]
                    .Move.PutAsync(
                        new Gen.MoveDocumentBlueprintRequestModel
                        {
                            // A null target moves the blueprint to the tree root.
                            Target = targetId is { } t
                                ? new Gen.ReferenceByIdModel { Id = t }
                                : null,
                        },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    // ── Folders ──────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<BlueprintFolderResponse>> GetBlueprintFolderAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var f = await _api
                    .Umbraco.Management.Api.V1.DocumentBlueprint.Folder[id]
                    .GetAsync(cancellationToken: ct);
                return new BlueprintFolderResponse { Id = f?.Id ?? id, Name = f?.Name ?? "" };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<BlueprintFolderResponse>> CreateBlueprintFolderAsync(
        CreateBlueprintFolderRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Client-generated id (the 201 body is empty), echoed back to the caller.
                var id = request.Id ?? Guid.NewGuid();
                await _api.Umbraco.Management.Api.V1.DocumentBlueprint.Folder.PostAsync(
                    new Gen.CreateFolderRequestModel
                    {
                        Id = id,
                        Name = request.Name,
                        Parent = request.Parent is { } p
                            ? new Gen.ReferenceByIdModel { Id = p.Id }
                            : null,
                    },
                    cancellationToken: ct
                );
                return new BlueprintFolderResponse { Id = id, Name = request.Name };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateBlueprintFolderAsync(
        Guid id,
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DocumentBlueprint.Folder[id]
                    .PutAsync(
                        new Gen.UpdateFolderResponseModel { Name = name },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteBlueprintFolderAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.DocumentBlueprint.Folder[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads a just-created blueprint so the returned JSON is fully hydrated. The create/from-document
    /// endpoints return an empty body, and the hydration read is best-effort: a failure still reports
    /// success (the create itself succeeded) by returning a minimal <c>{ "id": ... }</c> document.
    /// </summary>
    /// <param name="id">The created blueprint's id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hydrated blueprint JSON, or a minimal id-only object.</returns>
    private async Task<JsonNode> HydrateBlueprintAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await GetRawJsonAsync($"{BlueprintPath}/{id}", ct);
        }
        // Best-effort: a failed hydration read still reports the successful create. A cancellation,
        // however, must propagate rather than be masked as a hydration miss.
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new JsonObject { ["id"] = id.ToString() };
        }
    }
}
