using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// <see cref="IReferenceResolver"/>: one entry point, one strategy per kind (#250 Phase 3). Each
/// strategy reads the candidates from wherever the kind's alias or name really lives - never the
/// item search for an alias, since it only indexes names (#206, ADR 0004) - and hands them to
/// <see cref="ReferenceMatch.Pick"/>. Candidate lists are cached for the client's life, which is one
/// CLI invocation, so a command that resolves several references reads each list once.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    // Templates are few and the item endpoint returns aliases in bulk, so the whole list is read.
    private const int TemplateItemBatch = 40;

    private List<ReferenceCandidate>? _templateCandidates;
    private List<MediaTypeResponse>? _mediaTypes;
    private List<MemberTypeResponse>? _memberTypes;
    private List<ReferenceCandidate>? _memberGroupCandidates;
    private List<ReferenceCandidate>? _dictionaryCandidates;

    /// <inheritdoc />
    public Task<UmbracoResponse<Guid>> ResolveIdAsync(
        EntityKind kind,
        string reference,
        CancellationToken ct = default
    )
    {
        // An id needs no lookup, and must not cost a request.
        if (Guid.TryParse(reference, out var id))
            return Task.FromResult(UmbracoResponse<Guid>.Success(id));

        // Resolved already in this run (e.g. by a delete's in-use check, then by the delete).
        if (_resolved.TryGetValue((kind, reference), out var known))
            return Task.FromResult(UmbracoResponse<Guid>.Success(known));

        return ResolveUncachedAsync(kind, reference, ct);
    }

    /// <summary>
    /// <see cref="ResolveIdAsync"/> for the client's own methods that take a reference (a
    /// document type on create, a template, an upload's media type): the same single GUID check
    /// and lookup, with a failure raised as the <see cref="ApiException"/> their
    /// <see cref="GuardedApiAsync{T}"/> maps.
    /// </summary>
    /// <param name="kind">What kind of item the reference names.</param>
    /// <param name="reference">An id, alias, name or key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id.</returns>
    /// <exception cref="ApiException">The reference did not resolve.</exception>
    private async Task<Guid> IdOfAsync(EntityKind kind, string reference, CancellationToken ct)
    {
        var id = await ResolveIdAsync(kind, reference, ct);
        return id.IsSuccess
            ? id.Data
            : throw new ApiException(id.ErrorMessage ?? $"No {kind.Noun()} '{reference}'.")
            {
                ResponseStatusCode = id.StatusCode,
            };
    }

    /// <summary>Successful resolutions, by (kind, reference), for the client's life.</summary>
    private readonly Dictionary<(EntityKind, string), Guid> _resolved = [];

    private async Task<UmbracoResponse<Guid>> ResolveUncachedAsync(
        EntityKind kind,
        string reference,
        CancellationToken ct
    )
    {
        var result = await GuardedApiAsync(
            ct,
            () =>
                kind switch
                {
                    EntityKind.Template => FindTemplateIdAsync(reference, ct),
                    EntityKind.DocumentType => FindDocumentTypeIdAsync(reference, ct),
                    EntityKind.MediaType => FindMediaTypeIdAsync(reference, ct),
                    EntityKind.MemberType => FindMemberTypeIdAsync(reference, ct),
                    EntityKind.DataType => FindDataTypeIdAsync(reference, ct),
                    EntityKind.UserGroup => FindUserGroupIdAsync(reference, ct),
                    EntityKind.MemberGroup => FindMemberGroupIdAsync(reference, ct),
                    EntityKind.DictionaryItem => FindDictionaryIdAsync(reference, ct),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
                }
        );
        if (result.IsSuccess)
            _resolved[(kind, reference)] = result.Data;
        return result;
    }

    /// <summary>
    /// Resolves a template alias or name (#206). The item <i>search</i> matches names only, so
    /// <c>blogPost</c> (named "Blog Post") was never found; the tree gives every template's id,
    /// including those nested under a master, and the item endpoint gives their aliases.
    /// </summary>
    /// <param name="reference">The alias or name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The template id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous name (409).</exception>
    private async Task<Guid> FindTemplateIdAsync(string reference, CancellationToken ct) =>
        ReferenceMatch.Pick(EntityKind.Template, reference, await TemplateCandidatesAsync(ct));

    /// <summary>Every template, with its alias: a tree walk, then batched item reads.</summary>
    private async Task<List<ReferenceCandidate>> TemplateCandidatesAsync(CancellationToken ct)
    {
        if (_templateCandidates is not null)
            return _templateCandidates;

        // Templates nest under their master template, so every node with children is descended
        // into (not only folders), and every node is a template.
        var nodes = await CollectTreeAsync<(Guid Id, string? Name)>(
            async (parent, skip, take, c) =>
            {
                var page = parent is null
                    ? await _api.Umbraco.Management.Api.V1.Tree.Template.Root.GetAsync(
                        q =>
                        {
                            q.QueryParameters.Skip = skip;
                            q.QueryParameters.Take = take;
                        },
                        c
                    )
                    : await _api.Umbraco.Management.Api.V1.Tree.Template.Children.GetAsync(
                        q =>
                        {
                            q.QueryParameters.ParentId = parent;
                            q.QueryParameters.Skip = skip;
                            q.QueryParameters.Take = take;
                        },
                        c
                    );
                return
                [
                    .. (page?.Items ?? [])
                        .Where(t => t.Id is not null)
                        .Select(t =>
                            (t.Id!.Value, t.HasChildren == true, true, (t.Id!.Value, t.Name))
                        ),
                ];
            },
            ct
        );

        var aliases = new Dictionary<Guid, string?>();
        foreach (var batch in nodes.Chunk(TemplateItemBatch))
        {
            var items = await _api.Umbraco.Management.Api.V1.Item.Template.GetAsync(
                c => c.QueryParameters.Id = [.. batch.Select(n => (Guid?)n.Id)],
                ct
            );
            foreach (var item in items ?? [])
                if (item.Id is { } id)
                    aliases[id] = item.Alias;
        }

        return _templateCandidates = [
            .. nodes.Select(n => new ReferenceCandidate(
                n.Id,
                aliases.GetValueOrDefault(n.Id),
                n.Name
            )),
        ];
    }

    /// <summary>
    /// Resolves a media type alias or name (#222). Upload's <c>--media-type</c> used to match names
    /// only, which is why reads reported the name as the "alias"; now both work, alias first.
    /// </summary>
    /// <param name="reference">The alias or name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The media type id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous name (409).</exception>
    private async Task<Guid> FindMediaTypeIdAsync(string reference, CancellationToken ct) =>
        ReferenceMatch.Pick(EntityKind.MediaType, reference, await MediaTypeCandidatesAsync(ct));

    /// <summary>
    /// Every media type, with its alias. Neither the tree nor the item models carry the alias, so
    /// each type costs one by-id read; there are rarely more than a couple of dozen.
    /// </summary>
    private async Task<List<ReferenceCandidate>> MediaTypeCandidatesAsync(CancellationToken ct) =>
        [
            .. (await MediaTypesWithAliasAsync(ct)).Select(t => new ReferenceCandidate(
                t.Id,
                t.Alias,
                t.Name
            )),
        ];

    /// <summary>
    /// Every media type (folders excluded, nested types included) with its alias, read once per
    /// client. Shared by the resolver and <c>media-types list</c> (#221).
    /// </summary>
    private async Task<List<MediaTypeResponse>> MediaTypesWithAliasAsync(CancellationToken ct) =>
        _mediaTypes ??= await TypesWithAliasAsync(
            FetchMediaTypeTreeAsync,
            async (id, c) =>
                (
                    await _api
                        .Umbraco.Management.Api.V1.MediaType[id]
                        .GetAsync(cancellationToken: c)
                )?.Alias,
            (type, alias) => type with { Alias = alias },
            t => t.Id,
            _mediaTypeAliasById,
            ct
        );

    /// <summary>
    /// Every member type (folders excluded, nested types included) with its alias, read once per
    /// client, for <c>member-types list</c> (#213). The list read the tree root only before, so a
    /// type inside a folder was missing, and the alias was always <c>""</c>.
    /// </summary>
    private async Task<List<MemberTypeResponse>> MemberTypesWithAliasAsync(CancellationToken ct) =>
        _memberTypes ??= await TypesWithAliasAsync(
            FetchMemberTypeTreeAsync,
            async (id, c) =>
                (
                    await _api
                        .Umbraco.Management.Api.V1.MemberType[id]
                        .GetAsync(cancellationToken: c)
                )?.Alias,
            (type, alias) => type with { Alias = alias },
            t => t.Id,
            _memberTypeAliasById,
            ct
        );

    // Enough to overlap the by-id reads without hammering the server.
    private const int AliasReadConcurrency = 4;

    /// <summary>
    /// A type tree's every type with its alias. Neither the tree nor the item models carry the
    /// alias, so each type costs one by-id read; they run a few at a time, and the results are
    /// gathered before anything is written, so the alias cache is filled from one thread.
    /// </summary>
    /// <typeparam name="T">The type's response model.</typeparam>
    /// <param name="fetchTree">Fetches a page of the type tree.</param>
    /// <param name="readAlias">Reads one type's alias by id.</param>
    /// <param name="withAlias">Copies a type with its alias set.</param>
    /// <param name="idOf">The type's id.</param>
    /// <param name="aliasCache">The id-to-alias cache the read projections use, filled here too.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Every type, with its alias ("" when it could not be read).</returns>
    private static async Task<List<T>> TypesWithAliasAsync<T>(
        Func<
            Guid?,
            int,
            int,
            CancellationToken,
            Task<IReadOnlyList<(Guid Id, bool IsFolder, T Item)>>
        > fetchTree,
        Func<Guid, CancellationToken, Task<string?>> readAlias,
        Func<T, string, T> withAlias,
        Func<T, Guid> idOf,
        Dictionary<Guid, string> aliasCache,
        CancellationToken ct
    )
    {
        var types = await CollectTreeLeavesAsync(fetchTree, ct);
        using var gate = new SemaphoreSlim(AliasReadConcurrency);
        var aliases = await Task.WhenAll(
            types.Select(async type =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await readAlias(idOf(type), ct);
                }
                finally
                {
                    gate.Release();
                }
            })
        );

        var result = new List<T>(types.Count);
        for (var i = 0; i < types.Count; i++)
        {
            result.Add(withAlias(types[i], aliases[i] ?? ""));
            if (aliases[i] is { } alias)
                aliasCache[idOf(types[i])] = alias;
        }
        return result;
    }

    /// <summary>Resolves a user group alias or name (#217).</summary>
    /// <param name="reference">The alias or name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user group id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous name (409).</exception>
    private async Task<Guid> FindUserGroupIdAsync(string reference, CancellationToken ct) =>
        ReferenceMatch.Pick(
            EntityKind.UserGroup,
            reference,
            (await ReadAllUserGroupsAsync(ct))
                .Where(g => g.Id is not null)
                .Select(g => new ReferenceCandidate(g.Id!.Value, g.Alias, g.Name))
        );

    /// <summary>Resolves a member group name (#212). Member groups have no alias.</summary>
    /// <param name="reference">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member group id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous name (409).</exception>
    private async Task<Guid> FindMemberGroupIdAsync(string reference, CancellationToken ct) =>
        ReferenceMatch.Pick(
            EntityKind.MemberGroup,
            reference,
            await MemberGroupCandidatesAsync(ct)
        );

    /// <summary>Every member group, read once per client (the resolver and member labels, #212).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The groups as candidates (names; member groups have no alias).</returns>
    private async Task<List<ReferenceCandidate>> MemberGroupCandidatesAsync(CancellationToken ct) =>
        _memberGroupCandidates ??= await ReadAllPagesAsync(async (skip, take) => (
                    await _api.Umbraco.Management.Api.V1.Tree.MemberGroup.Root.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                )?.Items?.Where(i => i.Id is not null).Select(i => new ReferenceCandidate(i.Id!.Value, null, i.Name)).ToList() ?? []);

    /// <summary>
    /// Resolves a dictionary key (#211). Every page of the dictionary is read: the old lookup read
    /// the first 1,000 items and silently missed any key after them.
    /// </summary>
    /// <param name="reference">The key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dictionary item id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous key (409).</exception>
    private async Task<Guid> FindDictionaryIdAsync(string reference, CancellationToken ct)
    {
        _dictionaryCandidates ??= await ReadAllPagesAsync(async (skip, take) => (
                    await _api.Umbraco.Management.Api.V1.Dictionary.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                )?.Items?.Where(i => i.Id is not null).Select(i => new ReferenceCandidate(i.Id!.Value, null, i.Name)).ToList() ?? []);
        return ReferenceMatch.Pick(EntityKind.DictionaryItem, reference, _dictionaryCandidates);
    }

    /// <summary>Reads a paged source to the end, stopping on a short page.</summary>
    /// <param name="page">Reads one page: (skip, take).</param>
    /// <returns>Every item.</returns>
    private static async Task<List<ReferenceCandidate>> ReadAllPagesAsync(
        Func<int, int, Task<List<ReferenceCandidate>>> page
    )
    {
        const int take = 100;
        // A backstop against a server that never returns a short page.
        const int max = 100_000;
        var all = new List<ReferenceCandidate>();
        while (all.Count < max)
        {
            var items = await page(all.Count, take);
            all.AddRange(items);
            if (items.Count < take)
                break;
        }
        return all;
    }
}
