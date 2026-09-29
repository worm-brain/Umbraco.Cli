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
    private List<ReferenceCandidate>? _memberGroupCandidates;
    private List<ReferenceCandidate>? _dictionaryCandidates;
    private List<ReferenceCandidate>? _relationTypeCandidates;

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
        if (id.IsSuccess)
            return id.Data;
        var message = id.ErrorMessage ?? $"No {kind.Noun()} '{reference}'.";
        // Keep an unresolved reference distinguishable from a server failure, so the guard
        // around the calling method reports it as invalid_argument too (#256).
        throw id.Category == FailureCategory.InvalidArgument
            ? new UnresolvedReferenceException(message, 404)
            : new ApiException(message) { ResponseStatusCode = id.StatusCode };
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
                    EntityKind.RelationType => FindRelationTypeIdAsync(reference, ct),
                    EntityKind.Webhook => FindWebhookIdAsync(reference, ct),
                    EntityKind.User => FindUserIdAsync(reference, ct),
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
        await FindTypeIdAsync(
            EntityKind.MediaType,
            reference,
            [.. (await MediaTypeLeavesAsync(ct)).Select(t => (t.Id, t.Name))],
            ct
        );

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

    /// <summary>
    /// Resolves a relation type alias or name (#300). <c>relation-type list</c> shows the alias
    /// (<c>relateDocumentOnCopy</c>), so <c>relation list --relation-type</c> and
    /// <c>relation-type get</c> take it too. The paged list carries alias and name, so every page
    /// is read once; there are rarely more than a dozen types.
    /// </summary>
    /// <param name="reference">The alias or name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The relation type id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous name (409).</exception>
    private async Task<Guid> FindRelationTypeIdAsync(string reference, CancellationToken ct)
    {
        _relationTypeCandidates ??= await ReadAllPagesAsync(async (skip, take) => (
                    await _api.Umbraco.Management.Api.V1.RelationType.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                )?.Items?.Where(t => t.Id is not null).Select(t => new ReferenceCandidate(t.Id!.Value, t.Alias, t.Name)).ToList() ?? []);
        return ReferenceMatch.Pick(EntityKind.RelationType, reference, _relationTypeCandidates);
    }

    /// <summary>
    /// Resolves a user's email or username (#216). Users have no alias, so the email takes the
    /// alias's place - it is the key the backoffice shows and matches first - and the username the
    /// name's. Every page of users is read; the item search matches display names, which are not
    /// unique. Umbraco hides the super-user from other users, so it resolves only for itself.
    /// </summary>
    /// <param name="reference">The email or username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user id.</returns>
    /// <exception cref="ApiException">No match (404) or an ambiguous username (409).</exception>
    private async Task<Guid> FindUserIdAsync(string reference, CancellationToken ct)
    {
        var users = await ReadAllPagesAsync(async (skip, take) => (
                    await _api.Umbraco.Management.Api.V1.User.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    )
                )?.Items?.Where(u => u.Id is not null).Select(u => new ReferenceCandidate(u.Id!.Value, u.Email, u.UserName)).ToList() ?? []);
        return ReferenceMatch.Pick(EntityKind.User, reference, users);
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
