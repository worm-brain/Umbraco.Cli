using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Dictionary hierarchy support (issue #110): browsing the tree and reparenting items. Kept in its
/// own partial so these additions don't deepen the main client file; the dictionary CRUD stays with
/// the other list/get/create/delete verbs. Create-under-parent is a field on the existing create
/// request, so it lives with <c>CreateDictionaryItemAsync</c>.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Lists the dictionary tree via <c>tree/dictionary/root</c> or <c>tree/dictionary/children</c>
    /// (issue #110). There is no folder concept for dictionary items, so every row is a real item;
    /// each carries its parent id and whether it has children. Mirrors
    /// <see cref="GetDocumentBlueprintsAsync"/>.
    /// </summary>
    /// <param name="parentId">Parent id to list children of; null lists the root level.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of dictionary tree items, or a mapped failure.</returns>
    public Task<UmbracoResponse<PagedResponse<DictionaryTreeItem>>> GetDictionaryTreeAsync(
        Guid? parentId = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var tree = _api.Umbraco.Management.Api.V1.Tree.Dictionary;
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
                return new PagedResponse<DictionaryTreeItem>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new DictionaryTreeItem
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                            HasChildren = i.HasChildren ?? false,
                            Parent = i.Parent?.Id is { } pId
                                ? new ContentParentReference { Id = pId }
                                : null,
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>
    /// Reparents a dictionary item via <c>PUT dictionary/{id}/move</c> (issue #110). A null target
    /// moves the item to the dictionary root. Mirrors <see cref="MoveDocumentBlueprintAsync"/>.
    /// </summary>
    /// <param name="id">The dictionary item id to move.</param>
    /// <param name="targetId">Target parent id; null moves the item to the dictionary root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> MoveDictionaryItemAsync(
        Guid id,
        Guid? targetId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Dictionary[id]
                    .Move.PutAsync(
                        new Gen.MoveDictionaryRequestModel
                        {
                            Target = targetId is { } t
                                ? new Gen.ReferenceByIdModel { Id = t }
                                : null,
                        },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<DictionaryItemResponse>> UpdateDictionaryItemAsync(
        Guid id,
        UpdateDictionaryItemRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // #181's lesson: check the codes before sending, because Umbraco accepts the
                // request and silently discards translations whose language it does not know.
                await GuardDictionaryIsoCodesAsync(request.Translations, ct);

                var current =
                    await _api
                        .Umbraco.Management.Api.V1.Dictionary[id]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No dictionary item found with id '{id}'.");

                // The PUT replaces, so merge by ISO code: naming one language must not clear the
                // rest (#179's shape, one noun over).
                var merged = (current.Translations ?? [])
                    .Select(t => new Gen.DictionaryItemTranslationModel
                    {
                        IsoCode = t.IsoCode,
                        Translation = t.Translation,
                    })
                    .ToList();
                foreach (var t in request.Translations)
                {
                    var existing = merged.FindIndex(m =>
                        string.Equals(m.IsoCode, t.IsoCode, StringComparison.OrdinalIgnoreCase)
                    );
                    var entry = new Gen.DictionaryItemTranslationModel
                    {
                        IsoCode = t.IsoCode,
                        Translation = t.Translation,
                    };
                    if (existing >= 0)
                        merged[existing] = entry;
                    else
                        merged.Add(entry);
                }

                await _api
                    .Umbraco.Management.Api.V1.Dictionary[id]
                    .PutAsync(
                        new Gen.UpdateDictionaryItemRequestModel
                        {
                            Name = request.Name ?? current.Name ?? "",
                            Translations = merged,
                        },
                        cancellationToken: ct
                    );

                // Report what the instance kept, not what was asked for (#181).
                var stored = await ReadDictionaryItemAsync(id, ct);
                return stored.IsSuccess && stored.Data is { } item
                    ? item
                    : new DictionaryItemResponse { Id = id, Name = request.Name ?? "" };
            }
        );

    /// <summary>
    /// Reads a dictionary item by id, for the post-create read-back (#181). By id rather than by
    /// name: the by-key read lists every item and string-matches, which is both wasteful and
    /// ambiguous if two items share a name.
    /// </summary>
    /// <param name="id">The dictionary item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The item as the instance holds it, or a mapped failure.</returns>
    private Task<UmbracoResponse<DictionaryItemResponse>> ReadDictionaryItemAsync(
        Guid id,
        CancellationToken ct
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var d = await _api
                    .Umbraco.Management.Api.V1.Dictionary[id]
                    .GetAsync(cancellationToken: ct);
                return new DictionaryItemResponse
                {
                    Id = d?.Id ?? id,
                    Name = d?.Name ?? "",
                    Translations = (d?.Translations ?? [])
                        .Select(t => new DictionaryTranslation
                        {
                            IsoCode = t.IsoCode ?? "",
                            Translation = t.Translation ?? "",
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>
    /// Rejects translations whose ISO code is not a language on this instance (#181).
    /// <para>
    /// Umbraco accepts the create and silently drops those translations, so without this the
    /// caller is told the item saved and only finds out later that it is empty. Deliberately does
    /// not try to resolve a short code like <c>en</c> to <c>en-US</c>: on a site with both en-US
    /// and en-GB that guess is a coin flip, and guessing is what produced this class of bug.
    /// </para>
    /// </summary>
    /// <param name="translations">The requested translations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ApiException">A code does not match any configured language (mapped to 400).</exception>
    private async Task GuardDictionaryIsoCodesAsync(
        IEnumerable<DictionaryTranslation> translations,
        CancellationToken ct
    )
    {
        var requested = translations.Select(t => t.IsoCode).ToList();
        if (requested.Count == 0)
            return;

        // A blank code is rejected rather than filtered out: filtering would let it past the
        // guard to be discarded by Umbraco, which is the behaviour being fixed.
        if (requested.Any(string.IsNullOrWhiteSpace))
            throw BadRequest("A translation's ISO code cannot be empty.");

        var known = await KnownIsoCodesAsync(ct);

        // A language list we could not read is a failure to validate, not a pass - skipping the
        // check here would quietly restore the silent-discard behaviour.
        if (known is null)
            throw BadRequest(
                "Could not read this instance's languages to check the translation ISO codes. "
                    + "Umbraco discards translations whose code it does not recognise, so the "
                    + "request was not sent."
            );

        var unknown = requested.Where(c => !known.Contains(c)).ToList();
        if (unknown.Count > 0)
            throw BadRequest(
                $"This instance has no language with the ISO code {string.Join(", ", unknown.Select(u => $"'{u}'"))}. "
                    + $"Umbraco would accept the request and discard those translations. "
                    + $"Configured languages: {string.Join(", ", known.Order())}."
            );
    }
}
