using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// The by-id read projections (#187 Phase 3).
/// <para>
/// Each of these reads was fetching a rich body from the Management API and copying a handful of
/// fields into a hand-written record, dropping the rest at the mapping step - a document's values
/// (#168), a media item's file metadata and URL (#172), and every type reference's alias (#163).
/// The mappers and the type-label caches that fix that live here rather than in the main partial,
/// following the file-per-concern split the rest of the client already uses.
/// </para>
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Maps generated property values to the CLI-facing shape (#168/#172), converting each
    /// <c>UntypedNode</c> back to JSON. Null in, null out: a list or tree walk carries no values,
    /// and that is different from an item having none.
    /// </summary>
    /// <param name="values">The generated values, or null.</param>
    /// <returns>The mapped values, or null.</returns>
    private static List<ContentValueResponse>? MapValueResponses(
        List<Gen.DocumentValueResponseModel>? values
    ) =>
        values
            ?.Select(v => new ContentValueResponse
            {
                Alias = v.Alias ?? "",
                Culture = v.Culture,
                Segment = v.Segment,
                EditorAlias = v.EditorAlias,
                Value = UntypedNodeFactory.ToJsonNode(v.Value),
            })
            .ToList();

    /// <summary>
    /// Maps generated variants to the CLI-facing shape (#168), field for field: every property of
    /// the API's <c>DocumentVariantResponseModel</c>, including the schedule dates (#297) and the
    /// variant's <c>id</c> and <c>flags</c> (#306). <c>ContentReadModelWireTests</c> checks the
    /// list against the spec, so a field added to the API fails a test rather than being dropped.
    /// </summary>
    /// <param name="variants">The generated variants, or null.</param>
    /// <returns>The mapped variants, or null.</returns>
    private static List<ContentVariantResponse>? MapVariantResponses(
        List<Gen.DocumentVariantResponseModel>? variants
    ) =>
        variants
            ?.Select(v => new ContentVariantResponse
            {
                Culture = v.Culture,
                Segment = v.Segment,
                Name = v.Name ?? "",
                State = v.State?.ToString(),
                CreateDate = v.CreateDate,
                UpdateDate = v.UpdateDate,
                PublishDate = v.PublishDate,
                ScheduledPublishDate = v.ScheduledPublishDate,
                ScheduledUnpublishDate = v.ScheduledUnpublishDate,
                Id = v.Id,
                Flags = MapFlags(v.Flags),
            })
            .ToList();

    /// <summary>Maps the API's flags (#306). Null in, null out, so an endpoint without them adds no key.</summary>
    /// <param name="flags">The generated flags, or null.</param>
    /// <returns>The mapped flags, or null.</returns>
    private static List<FlagResponse>? MapFlags(List<Gen.FlagModel>? flags) =>
        flags?.Select(f => new FlagResponse { Alias = f.Alias ?? "" }).ToList();

    /// <summary>
    /// Maps the API's document-type reference with every field it carries (#306): id, icon and
    /// collection. The alias is not on the reference; <see cref="DocumentTypeAliasAsync"/> fills it.
    /// </summary>
    /// <param name="type">The generated reference, or null.</param>
    /// <returns>The mapped reference, or null when there is none or it has no id.</returns>
    private static ContentTypeRef? MapDocumentTypeRef(
        Gen.DocumentTypeReferenceResponseModel? type
    ) =>
        type?.Id is { } id
            ? new ContentTypeRef
            {
                Id = id,
                Icon = type.Icon,
                Collection = type.Collection?.Id is { } collection
                    ? new ContentParentReference { Id = collection }
                    : null,
            }
            : null;

    /// <summary>Document-type id to alias, for #163. Filled in on first use, then reused.</summary>
    private readonly Dictionary<Guid, string> _documentTypeAliasById = [];

    /// <summary>
    /// Resolves a document type's alias from its id (#163).
    /// <para>
    /// The Management API's type reference on a document carries only an id, so the alias has to
    /// be looked up. Previously the field was simply left as <c>""</c>, which read as "this type
    /// has no alias" rather than "nobody asked". One read per distinct type, cached for the life
    /// of the client, and a failure yields null so the field is omitted rather than faked.
    /// </para>
    /// </summary>
    /// <param name="id">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The alias, or null when it could not be read.</returns>
    private Task<string?> DocumentTypeAliasAsync(Guid id, CancellationToken ct) =>
        CachedTypeLabelAsync(
            _documentTypeAliasById,
            id,
            async token =>
                (
                    await _api
                        .Umbraco.Management.Api.V1.DocumentType[id]
                        .GetAsync(cancellationToken: token)
                )?.Alias,
            ct
        );

    /// <summary>
    /// Adds the alias to a raw read's <c>documentType</c> reference, which carries only
    /// id/icon/collection, so one jq filter reads a raw read (version get, blueprint get and
    /// scaffold) as it reads <c>content get</c> and <c>list</c> (#320, #361). A null collection is
    /// dropped, as the mapped reads omit it. The alias is left out when it cannot be read.
    /// </summary>
    /// <param name="obj">The raw read; its <c>documentType</c> is changed in place.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the alias has been added (or could not be).</returns>
    private async Task AddDocumentTypeAliasAsync(JsonObject obj, CancellationToken ct)
    {
        if (obj["documentType"] is not JsonObject type)
            return;
        if (type["collection"] is null && type.ContainsKey("collection"))
            type.Remove("collection");
        if (
            type["id"]?.GetValue<string>() is { } rawType
            && Guid.TryParse(rawType, out var typeId)
            && await DocumentTypeAliasAsync(typeId, ct) is { } alias
        )
            type["alias"] = alias;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyDictionary<Guid, string>>> GetDocumentTypeAliasesAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync<IReadOnlyDictionary<Guid, string>>(
            ct,
            async () =>
            {
                // One at a time: the alias cache is not thread-safe, and a diff rarely names more
                // than a handful of types.
                var aliases = new Dictionary<Guid, string>();
                foreach (var id in ids.Distinct())
                    if (await DocumentTypeAliasAsync(id, ct) is { } alias)
                        aliases[id] = alias;
                return aliases;
            }
        );

    /// <summary>
    /// Reads a label (alias or name) for a type id, caching it for the life of the client.
    /// <para>
    /// Only successes are cached. Caching a failure would mean one transient 503 during a bulk
    /// run silently stripped the label from every later item for the rest of the process, which
    /// is worse than re-asking.
    /// </para>
    /// </summary>
    /// <param name="cache">The per-kind cache to read and fill.</param>
    /// <param name="id">The type id.</param>
    /// <param name="fetch">Reads the label from the API.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The label, or null when it could not be read.</returns>
    private static async Task<string?> CachedTypeLabelAsync(
        Dictionary<Guid, string> cache,
        Guid id,
        Func<CancellationToken, Task<string?>> fetch,
        CancellationToken ct
    )
    {
        if (cache.TryGetValue(id, out var cached))
            return cached;

        try
        {
            // A type that cannot be read (deleted, no permission, a blip) must not fail the read
            // that only wanted its label.
            if (await fetch(ct) is { } label)
            {
                cache[id] = label;
                return label;
            }
        }
        catch (ApiException) { }

        return null;
    }

    /// <summary>Maps generated media values to the CLI-facing shape (#172).</summary>
    /// <param name="values">The generated values, or null.</param>
    /// <returns>The mapped values, or null.</returns>
    private static List<ContentValueResponse>? MapMediaValueResponses(
        List<Gen.MediaValueResponseModel>? values
    ) =>
        values
            ?.Select(v => new ContentValueResponse
            {
                Alias = v.Alias ?? "",
                Culture = v.Culture,
                Segment = v.Segment,
                EditorAlias = v.EditorAlias,
                Value = UntypedNodeFactory.ToJsonNode(v.Value),
            })
            .ToList();

    /// <summary>Member-type id to alias, for #212.</summary>
    private readonly Dictionary<Guid, string> _memberTypeAliasById = [];

    /// <summary>
    /// Fills in what member reads carry only as ids (#212): each member type's alias, as content
    /// reads already do (#163), and each group's name. The labels are read <b>once for the whole
    /// batch</b> - one group list, one alias read per distinct member type, one after another - and
    /// then applied, so a page of members neither repeats reads nor touches the caches from
    /// several tasks at once. A label that cannot be read is left null rather than failing the read.
    /// </summary>
    /// <param name="members">The mapped members.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The members with their labels, in the same order.</returns>
    private async Task<List<MemberResponse>> LabelMembersAsync(
        IReadOnlyList<MemberResponse> members,
        CancellationToken ct
    )
    {
        var aliases = new Dictionary<Guid, string?>();
        foreach (var typeId in members.Select(m => m.MemberType?.Id).OfType<Guid>().Distinct())
            aliases[typeId] = await MemberTypeAliasAsync(typeId, ct);

        List<ReferenceCandidate>? groups = null;
        if (members.Any(m => m.Groups?.Any() == true))
        {
            try
            {
                groups = await MemberGroupCandidatesAsync(ct);
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException)
            {
                // The ids are still right; only the names are missing.
            }
        }

        return
        [
            .. members.Select(member =>
                member with
                {
                    MemberType = member.MemberType is { } mt
                        ? mt with
                        {
                            Alias = aliases.GetValueOrDefault(mt.Id),
                        }
                        : null,
                    Groups = member
                        .Groups?.Select(g =>
                            g with
                            {
                                Name = groups?.FirstOrDefault(c => c.Id == g.Id)?.Name,
                            }
                        )
                        .ToList(),
                }
            ),
        ];
    }

    /// <summary>Labels one member; see <see cref="LabelMembersAsync"/>.</summary>
    /// <param name="member">The mapped member.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member with its labels.</returns>
    private async Task<MemberResponse> LabelMemberAsync(
        MemberResponse member,
        CancellationToken ct
    ) => (await LabelMembersAsync([member], ct))[0];

    /// <summary>Resolves a member type's alias from its id (#212), cached for the client's life.</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The alias, or null when it could not be read.</returns>
    private Task<string?> MemberTypeAliasAsync(Guid id, CancellationToken ct) =>
        CachedTypeLabelAsync(
            _memberTypeAliasById,
            id,
            async token =>
                (
                    await _api
                        .Umbraco.Management.Api.V1.MemberType[id]
                        .GetAsync(cancellationToken: token)
                )?.Alias,
            ct
        );

    /// <summary>Media-type id to alias, for #163 / #222.</summary>
    private readonly Dictionary<Guid, string> _mediaTypeAliasById = [];

    /// <summary>
    /// Resolves a media type's alias from its id (#163, #222), cached for the client's life.
    /// </summary>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The alias, or null when it could not be read.</returns>
    private Task<string?> MediaTypeAliasAsync(Guid id, CancellationToken ct) =>
        CachedTypeLabelAsync(
            _mediaTypeAliasById,
            id,
            async token =>
                (
                    await _api
                        .Umbraco.Management.Api.V1.MediaType[id]
                        .GetAsync(cancellationToken: token)
                )?.Alias,
            ct
        );

    /// <summary>
    /// Reads a media item's public URLs (#172) via <c>GET media/urls</c>. The URL is not on the
    /// by-id body, which is why <c>media get</c> could not show it despite its help saying so.
    /// A failure yields null rather than failing the whole read.
    /// </summary>
    /// <param name="id">The media id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The URLs, or null when they could not be read.</returns>
    private async Task<List<UrlInfo>?> MediaUrlsAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var urls = await _api.Umbraco.Management.Api.V1.Media.Urls.GetAsync(
                c => c.QueryParameters.Id = [id],
                ct
            );
            return (urls ?? [])
                .SelectMany(u => u.UrlInfos ?? [])
                .Select(u => new UrlInfo { Culture = u.Culture, Url = u.Url ?? "" })
                .ToList();
        }
        catch (ApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a document's public URLs (#289) via <c>GET document/urls</c>, the twin of
    /// <see cref="MediaUrlsAsync"/>: the by-id body has no URL, so a script checking a publish on
    /// the front end had to build the route by hand. One entry per culture. A failure yields null
    /// (the key is left out) rather than failing the whole read.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The URLs, or null when they could not be read.</returns>
    private async Task<List<UrlInfo>?> DocumentUrlsAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var urls = await _api.Umbraco.Management.Api.V1.Document.Urls.GetAsync(
                c => c.QueryParameters.Id = [id],
                ct
            );
            return (urls ?? [])
                .SelectMany(u => u.UrlInfos ?? [])
                .Select(u => new UrlInfo { Culture = u.Culture, Url = u.Url ?? "" })
                .ToList();
        }
        catch (ApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fills each row's document-type alias (#202): tree rows carry only the type id, so a list
    /// said <c>documentType: {id}</c> where <c>get</c> said <c>{id, alias}</c>.
    /// </summary>
    /// <param name="rows">The mapped rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows, with aliases filled where they could be read.</returns>
    private Task<List<ContentItemResponse>> WithDocumentTypeAliasesAsync(
        IEnumerable<ContentItemResponse> rows,
        CancellationToken ct
    ) =>
        WithTypeAliasesAsync(
            rows,
            r => r.DocumentType,
            (r, type) => r with { DocumentType = type },
            DocumentTypeAliasAsync,
            ct
        );

    /// <summary>The media twin of <see cref="WithDocumentTypeAliasesAsync"/> (#202).</summary>
    /// <param name="rows">The mapped rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows, with aliases filled where they could be read.</returns>
    private Task<List<MediaItemResponse>> WithMediaTypeAliasesAsync(
        IEnumerable<MediaItemResponse> rows,
        CancellationToken ct
    ) =>
        WithTypeAliasesAsync(
            rows,
            r => r.MediaType,
            (r, type) => r with { MediaType = type },
            MediaTypeAliasAsync,
            ct
        );

    /// <summary>
    /// Fills a type alias on each row from a cached lookup: one read per distinct type, made one
    /// at a time because the alias caches are not thread-safe.
    /// </summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="rows">The rows.</param>
    /// <param name="typeOf">Reads a row's type reference.</param>
    /// <param name="withType">Returns the row with a new type reference.</param>
    /// <param name="aliasOf">The cached alias lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows, with aliases filled.</returns>
    private static async Task<List<TRow>> WithTypeAliasesAsync<TRow>(
        IEnumerable<TRow> rows,
        Func<TRow, ContentTypeRef?> typeOf,
        Func<TRow, ContentTypeRef, TRow> withType,
        Func<Guid, CancellationToken, Task<string?>> aliasOf,
        CancellationToken ct
    )
    {
        var result = new List<TRow>();
        foreach (var row in rows)
            result.Add(
                typeOf(row) is { } type
                    ? withType(row, type with { Alias = await aliasOf(type.Id, ct) })
                    : row
            );
        return result;
    }

    /// <summary>
    /// A document's parent (#205). <c>GET /document/{id}</c> has no parent field, so it is read from
    /// <c>GET /tree/document/ancestors</c>. Best-effort - a failed read leaves the parent out rather
    /// than failing the <c>get</c>.
    /// </summary>
    /// <param name="id">The document id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parent, or null at the root or when it could not be read.</returns>
    private Task<ContentParentReference?> DocumentParentAsync(Guid id, CancellationToken ct) =>
        ParentFromTreeAsync(
            id,
            async token =>
                (
                    await _api.Umbraco.Management.Api.V1.Tree.Document.Ancestors.GetAsync(
                        c => c.QueryParameters.DescendantId = id,
                        token
                    )
                )?.Select(a => (a.Id, a.Parent?.Id)),
            ct
        );

    /// <summary>A media item's parent (#205); see <see cref="DocumentParentAsync"/>.</summary>
    /// <param name="id">The media item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parent, or null at the root or when it could not be read.</returns>
    private Task<ContentParentReference?> MediaParentAsync(Guid id, CancellationToken ct) =>
        ParentFromTreeAsync(
            id,
            async token =>
                (
                    await _api.Umbraco.Management.Api.V1.Tree.Media.Ancestors.GetAsync(
                        c => c.QueryParameters.DescendantId = id,
                        token
                    )
                )?.Select(a => (a.Id, a.Parent?.Id)),
            ct
        );

    /// <summary>
    /// A dictionary item's parent (#290). <c>GET /dictionary/{id}</c> has no parent field, so it is
    /// read from <c>GET /tree/dictionary/ancestors</c>, as for documents; best-effort.
    /// </summary>
    /// <param name="id">The dictionary item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parent, or null at the root or when it could not be read.</returns>
    private Task<ContentParentReference?> DictionaryParentAsync(Guid id, CancellationToken ct) =>
        ParentFromTreeAsync(
            id,
            async token =>
                (
                    await _api.Umbraco.Management.Api.V1.Tree.Dictionary.Ancestors.GetAsync(
                        c => c.QueryParameters.DescendantId = id,
                        token
                    )
                )?.Select(a => (a.Id, a.Parent?.Id)),
            ct
        );

    /// <summary>
    /// A document blueprint's parent folder (#298), from <c>GET /tree/document-blueprint/ancestors</c>
    /// (the blueprint body has no parent field); best-effort.
    /// </summary>
    /// <param name="id">The blueprint id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parent folder, or null at the root or when it could not be read.</returns>
    private Task<ContentParentReference?> BlueprintParentAsync(Guid id, CancellationToken ct) =>
        ParentFromTreeAsync(
            id,
            async token =>
                (
                    await _api.Umbraco.Management.Api.V1.Tree.DocumentBlueprint.Ancestors.GetAsync(
                        c => c.QueryParameters.DescendantId = id,
                        token
                    )
                )?.Select(a => (a.Id, a.Parent?.Id)),
            ct
        );

    /// <summary>Reads an ancestor chain and picks the parent out of it, best-effort.</summary>
    /// <param name="id">The item.</param>
    /// <param name="chain">Reads the chain as (id, parent id) pairs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parent, or null at the root or when the chain could not be read.</returns>
    private static async Task<ContentParentReference?> ParentFromTreeAsync(
        Guid id,
        Func<CancellationToken, Task<IEnumerable<(Guid? Id, Guid? ParentId)>?>> chain,
        CancellationToken ct
    )
    {
        try
        {
            return ParentIn(await chain(ct), id);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// Picks an item's parent out of its ancestor chain: the item's own entry names it; if the
    /// chain leaves the item out, its last entry is the parent.
    /// </summary>
    /// <param name="chain">Each ancestor's (id, parent id), root first.</param>
    /// <param name="id">The item whose parent is wanted.</param>
    /// <returns>The parent, or null at the root.</returns>
    internal static ContentParentReference? ParentIn(
        IEnumerable<(Guid? Id, Guid? ParentId)>? chain,
        Guid id
    )
    {
        var entries = chain?.ToList() ?? [];
        var parentId = entries.Any(e => e.Id == id)
            ? entries.First(e => e.Id == id).ParentId
            : entries.LastOrDefault().Id;
        return parentId is { } p ? new ContentParentReference { Id = p } : null;
    }
}
