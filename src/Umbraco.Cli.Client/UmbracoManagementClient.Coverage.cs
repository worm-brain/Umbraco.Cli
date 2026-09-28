using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Small coverage resources on <see cref="UmbracoManagementClient"/> - member groups (full CRUD),
/// and read-only tag and culture listings (issue #107). Kept in their own partial so the main
/// client file stays focused.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    // ── Member groups ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<MemberGroupResponse>>> GetMemberGroupsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tree.MemberGroup.Root.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<MemberGroupResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(i => new MemberGroupResponse
                        {
                            Id = i.Id ?? Guid.Empty,
                            Name = i.Name ?? "",
                        })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<MemberGroupResponse>> GetMemberGroupByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // A 200 with no body is not a member group (#119).
                var g =
                    await _api
                        .Umbraco.Management.Api.V1.MemberGroup[id]
                        .GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No member group found with id '{id}'.");
                return new MemberGroupResponse { Id = g.Id ?? id, Name = g.Name ?? "" };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<MemberGroupResponse>> CreateMemberGroupAsync(
        CreateMemberGroupRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Client-generated id (Umbraco 14+ accepts a supplied GUID); the 201 body is empty,
                // so echo the created group without a follow-up read.
                var id = request.Id ?? Guid.NewGuid();
                await _api.Umbraco.Management.Api.V1.MemberGroup.PostAsync(
                    new Gen.CreateMemberGroupRequestModel { Id = id, Name = request.Name },
                    cancellationToken: ct
                );
                return new MemberGroupResponse { Id = id, Name = request.Name };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateMemberGroupAsync(
        Guid id,
        UpdateMemberGroupRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.MemberGroup[id]
                    .PutAsync(
                        new Gen.UpdateMemberGroupRequestModel { Name = request.Name },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteMemberGroupAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.MemberGroup[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Tags (read-only) ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<TagResponse>>> GetTagsAsync(
        string? tagGroup = null,
        string? culture = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Tag.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                        if (!string.IsNullOrEmpty(tagGroup))
                            c.QueryParameters.TagGroup = tagGroup;
                        if (!string.IsNullOrEmpty(culture))
                            c.QueryParameters.Culture = culture;
                    },
                    ct
                );
                return new PagedResponse<TagResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(t => new TagResponse
                        {
                            Id = t.Id ?? Guid.Empty,
                            Text = t.Text ?? "",
                            Group = t.Group ?? "",
                            NodeCount = t.NodeCount ?? 0,
                        })
                        .ToList(),
                };
            }
        );

    // ── Cultures (read-only) ───────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<CultureResponse>>> GetCulturesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Culture.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<CultureResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(c => new CultureResponse
                        {
                            IsoCode = c.Name ?? "",
                            EnglishName = c.EnglishName ?? "",
                        })
                        .ToList(),
                };
            }
        );
}
