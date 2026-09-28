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
                // One row at a time: the tree row carries only the type id, so the alias is looked
                // up from the (not thread-safe) cache, as content list does (#361).
                var items = new List<DocumentBlueprintTreeItem>();
                foreach (var i in paged?.Items ?? [])
                    items.Add(
                        new DocumentBlueprintTreeItem
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            DocumentType = i.DocumentType?.Id is { } dtId
                                ? new ContentTypeRef
                                {
                                    Id = dtId,
                                    Alias = await DocumentTypeAliasAsync(dtId, ct),
                                }
                                : null,
                            IsFolder = i.IsFolder ?? false,
                            HasChildren = i.HasChildren ?? false,
                            Parent = i.Parent?.Id is { } pId
                                ? new ContentParentReference { Id = pId }
                                : null,
                        }
                    );
                return new PagedResponse<DocumentBlueprintTreeItem>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = items,
                };
            }
        );

    // ── Read (raw, full fidelity) ────────────────────────────────────────────────

    /// <summary>
    /// Reads a blueprint as the API returns it, plus the top-level <c>name</c> and <c>parent</c>
    /// the CLI adds to content reads (#298; see <see cref="WithBlueprintNameAndParentAsync"/>).
    /// </summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The blueprint JSON, or a mapped failure.</returns>
    public Task<UmbracoResponse<JsonNode>> GetDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
                await WithBlueprintNameAndParentAsync(
                    await GetRawJsonAsync($"{BlueprintPath}/{id}", ct),
                    id,
                    ct
                )
        );

    /// <summary>
    /// Adds what a blueprint body lacks and <c>content get</c> shows (#298): a top-level
    /// <c>name</c>, the first variant's (the same rule as <c>content get</c>), and a
    /// <c>parent: {id}</c> read from the blueprint tree. A blueprint at the root gets no
    /// <c>parent</c>, as a root document has none; a name already on the body is kept. The
    /// <c>documentType</c> reference also gains its alias (#361).
    /// </summary>
    /// <param name="blueprint">The blueprint as read; changed in place when it is an object.</param>
    /// <param name="id">The blueprint id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The same node, with the name and parent added.</returns>
    private async Task<JsonNode> WithBlueprintNameAndParentAsync(
        JsonNode blueprint,
        Guid id,
        CancellationToken ct
    )
    {
        if (blueprint is not JsonObject obj)
            return blueprint;

        if (
            obj["name"] is null
            && obj["variants"] is JsonArray { Count: > 0 } variants
            && variants[0]?["name"] is JsonValue name
        )
            obj["name"] = name.DeepClone();

        if (await BlueprintParentAsync(id, ct) is { } parent)
            obj["parent"] = new JsonObject { ["id"] = parent.Id.ToString() };

        // The document type's alias, as content get shows it (#361).
        await AddDocumentTypeAliasAsync(obj, ct);
        return obj;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<JsonNode>> ScaffoldDocumentBlueprintAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var body = await GetRawJsonAsync($"{BlueprintPath}/{id}/scaffold", ct);

                // The scaffold is a body for a NEW document, but Umbraco returns it with the
                // blueprint's own id. Dropped here, so a create can keep a body's id like every
                // other create does (#299) and a piped scaffold still makes a new document.
                if (body is JsonObject obj)
                {
                    obj.Remove("id");

                    // The document type's alias, as content get shows it (#361). A create reads
                    // the type by its id when both are given, so the piped body is unaffected.
                    await AddDocumentTypeAliasAsync(obj, ct);
                }
                return body;
            }
        );

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
                var variants = await FillCultureAsync(
                    request.Variants,
                    () => DocumentTypeCultureAsync(documentTypeId, ct)
                );

                var body = new Gen.CreateDocumentBlueprintRequestModel
                {
                    Id = id,
                    DocumentType = new Gen.ReferenceByIdModel { Id = documentTypeId },
                    Parent = request.Parent is { } p
                        ? new Gen.ReferenceByIdModel { Id = p.Id }
                        : null,
                    Variants = MapVariants(variants),
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
    /// <remarks>
    /// Umbraco 17.7 honours only part of the from-document request (#240): it creates the blueprint
    /// at the root whatever <c>parent</c> says, and names only the default-language variant, leaving
    /// the others with the source document's names. So after the create the blueprint is moved under
    /// the requested parent and every variant is renamed, and the result carries the name at the top
    /// level (a blueprint body has names only on its variants).
    /// </remarks>
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

                // The blueprint now exists, so a failure from here on must say so and name it:
                // otherwise a retry without --id makes a second one.
                var step = "";
                try
                {
                    // The parent in the body is ignored, so put the blueprint where it was asked
                    // to go. The blueprint body has no parent to check first, so it always moves.
                    if (request.Parent is { } parent)
                    {
                        step = $"was left at the root: moving it under {parent.Id} failed";
                        await _api
                            .Umbraco.Management.Api.V1.DocumentBlueprint[id]
                            .Move.PutAsync(
                                new Gen.MoveDocumentBlueprintRequestModel
                                {
                                    Target = new Gen.ReferenceByIdModel { Id = parent.Id },
                                },
                                cancellationToken: ct
                            );
                    }

                    step = "kept the source document's names: renaming its variants failed";
                    await RenameEveryVariantAsync(id, request.Name, ct);
                }
                catch (ApiException ex)
                {
                    var (status, message) = Describe(ex);
                    throw new ApiException(
                        $"Blueprint {id} was created, but {step}: {message.TrimEnd('.')}. "
                            + "Fix it with 'document-blueprint move' or 'document-blueprint update'."
                    )
                    {
                        ResponseStatusCode = status,
                    };
                }

                var hydrated = await HydrateBlueprintAsync(id, ct);
                if (hydrated is JsonObject result && request.Name is { Length: > 0 } name)
                    result["name"] = name;
                return hydrated;
            }
        );

    /// <summary>
    /// Gives every variant of a blueprint the same name (#240), in one merged PUT. Nothing is sent
    /// when every variant already has the name, which is the invariant case after a from-document
    /// create.
    /// </summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="name">The name to apply; nothing is done when it is empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the blueprint is renamed.</returns>
    private async Task RenameEveryVariantAsync(Guid id, string? name, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(name))
            return;

        var path = $"{BlueprintPath}/{id}";
        if (await GetRawJsonAsync(path, ct) is not JsonObject blueprint)
            return;

        var stale = (blueprint["variants"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(v => v["name"]?.GetValue<string?>() != name)
            .Select(v => new ContentVariant
            {
                Culture = v["culture"]?.GetValue<string?>(),
                Segment = v["segment"]?.GetValue<string?>(),
                Name = name,
            })
            .ToList();
        if (stale.Count == 0)
            return;

        DocumentUpdateBody.Merge(blueprint, [], stale, WriteMode.Merge);
        await SendRawJsonAsync(Method.PUT, path, blueprint, ct);
    }

    // ── Mutate ───────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// The PUT replaces the whole body, so sending only the supplied values dropped every other
    /// field (#242: featuredImage and publishDate vanished). Like <c>content update</c>, the
    /// blueprint is read and the request overlaid on it with <see cref="DocumentUpdateBody.Merge"/>
    /// unless the mode is <see cref="WriteMode.Replace"/>.
    /// </remarks>
    public Task<UmbracoResponse<Empty>> UpdateDocumentBlueprintAsync(
        Guid id,
        UpdateDocumentBlueprintRequest request,
        WriteMode mode = WriteMode.Merge,
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
                var variants = await FillCultureAsync(
                    request.Variants,
                    () => ExistingItemCultureAsync(blueprint["variants"] as JsonArray, ct)
                );
                DocumentUpdateBody.Merge(blueprint, request.Values, variants, mode);
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
                // A 200 with no body is not a folder (#119).
                var f =
                    await _api
                        .Umbraco.Management.Api.V1.DocumentBlueprint.Folder[id]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No document blueprint folder found with id '{id}'.");
                return new BlueprintFolderResponse { Id = f.Id ?? id, Name = f.Name ?? "" };
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
    /// Re-reads a just-created blueprint so the returned JSON is fully hydrated, as <c>get</c> shows
    /// it (name and parent included, #298). The create/from-document endpoints return an empty
    /// body, and the hydration read is best-effort: a failure still reports success (the create
    /// itself succeeded) by returning a minimal <c>{ "id": ... }</c> document.
    /// </summary>
    /// <param name="id">The created blueprint's id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hydrated blueprint JSON, or a minimal id-only object.</returns>
    private async Task<JsonNode> HydrateBlueprintAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await WithBlueprintNameAndParentAsync(
                await GetRawJsonAsync($"{BlueprintPath}/{id}", ct),
                id,
                ct
            );
        }
        // Best-effort: a failed hydration read still reports the successful create. A cancellation,
        // however, must propagate rather than be masked as a hydration miss.
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new JsonObject { ["id"] = id.ToString() };
        }
    }
}
