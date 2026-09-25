using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Redirect and relation resources on <see cref="UmbracoManagementClient"/> (issue #118). Redirects
/// support list/inspect, delete, and a tracking toggle; relation-type and relation are read-only in
/// the generated client. Kept in their own partial so the main client file stays focused.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    // ── Redirects ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<RedirectResponse>>> GetRedirectsAsync(
        string? filter = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.RedirectManagement.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                        if (!string.IsNullOrEmpty(filter))
                            c.QueryParameters.Filter = filter;
                    },
                    ct
                );
                return MapRedirects(paged);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<RedirectResponse>>> GetRedirectsForContentAsync(
        Guid contentKey,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api
                    .Umbraco.Management.Api.V1.RedirectManagement[contentKey]
                    .GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return MapRedirects(paged);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<RedirectStatusResponse>> GetRedirectStatusAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var s = await _api.Umbraco.Management.Api.V1.RedirectManagement.Status.GetAsync(
                    cancellationToken: ct
                );
                return new RedirectStatusResponse
                {
                    Enabled = s?.Status == Gen.RedirectStatusModel.Enabled,
                    UserIsAdmin = s?.UserIsAdmin ?? false,
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteRedirectAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.RedirectManagement[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Sets redirect tracking, then re-reads the status and fails if it did not change (#249).
    /// <para>
    /// Umbraco 17 answers <c>POST redirect-management/status</c> with 200 and can leave the status
    /// as it was - tracking is governed by configuration there - so a 200 alone is not proof the
    /// change happened. Reporting success on it told the caller something untrue.
    /// </para>
    /// </summary>
    /// <param name="enabled">The tracking state to set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success when the re-read status matches; otherwise an <c>unexpected_response</c> failure
    /// naming the configuration setting, or the failure of the write or the re-read.
    /// </returns>
    public async Task<UmbracoResponse<Empty>> SetRedirectTrackingAsync(
        bool enabled,
        CancellationToken ct = default
    )
    {
        var set = await GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.RedirectManagement.Status.PostAsync(
                    c =>
                        c.QueryParameters.Status = enabled
                            ? Gen.RedirectStatusModel.Enabled
                            : Gen.RedirectStatusModel.Disabled,
                    ct
                );
                return Empty.Value;
            }
        );
        if (!set.IsSuccess)
            return set;

        var status = await GetRedirectStatusAsync(ct);
        if (!status.IsSuccess)
            return UmbracoResponse<Empty>.FailureFrom(status);
        if (status.Data!.Enabled == enabled)
            return set;

        // The server accepted the request (200) and ignored it: its answer does not match what
        // the CLI asked for, hence unexpected_response rather than a rejected request.
        var kept = status.Data.Enabled ? "enabled" : "disabled";
        return UmbracoResponse<Empty>.Failure(
            200,
            $"Umbraco accepted the request but kept URL-redirect tracking {kept}. On Umbraco 17 "
                + "tracking is set by configuration: set "
                + $"Umbraco:CMS:WebRouting:DisableRedirectUrlTracking to {(!enabled).ToString().ToLowerInvariant()} "
                + "in appsettings and restart the site.",
            FailureCategory.UnexpectedResponse
        );
    }

    /// <summary>Maps a generated paged redirect response to the command-facing paged DTO.</summary>
    /// <param name="paged">The generated paged model.</param>
    /// <returns>The mapped <see cref="PagedResponse{T}"/>.</returns>
    private static PagedResponse<RedirectResponse> MapRedirects(
        Gen.PagedRedirectUrlResponseModel? paged
    ) =>
        new()
        {
            Total = (int)(paged?.Total ?? 0),
            Items = (paged?.Items ?? [])
                .Select(r => new RedirectResponse
                {
                    Id = r.Id ?? Guid.Empty,
                    OriginalUrl = r.OriginalUrl ?? "",
                    DestinationUrl = r.DestinationUrl ?? "",
                    Culture = r.Culture,
                    ContentKey = r.Document?.Id,
                    Created = r.Created ?? default,
                })
                .ToList(),
        };

    // ── Relation types ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<RelationTypeResponse>>> GetRelationTypesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.RelationType.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<RelationTypeResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapRelationType).ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<RelationTypeResponse>> GetRelationTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var t = await _api
                    .Umbraco.Management.Api.V1.RelationType[id]
                    .GetAsync(cancellationToken: ct);
                return t is null ? new RelationTypeResponse { Id = id } : MapRelationType(t);
            }
        );

    /// <summary>Maps a generated relation-type model to the command-facing DTO.</summary>
    /// <param name="t">The generated model.</param>
    /// <returns>The mapped <see cref="RelationTypeResponse"/>.</returns>
    private static RelationTypeResponse MapRelationType(Gen.RelationTypeResponseModel t) =>
        new()
        {
            Id = t.Id ?? Guid.Empty,
            Alias = t.Alias ?? "",
            Name = t.Name ?? "",
            IsBidirectional = t.IsBidirectional ?? false,
            IsDependency = t.IsDependency ?? false,
            ParentObjectType = t.ParentObject?.Name,
            ChildObjectType = t.ChildObject?.Name,
        };

    // ── Relations ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<RelationResponse>>> GetRelationsByTypeAsync(
        Guid relationTypeId,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api
                    .Umbraco.Management.Api.V1.Relation.Type[relationTypeId]
                    .GetAsync(
                        c =>
                        {
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<RelationResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapRelation).ToList(),
                };
            }
        );

    /// <summary>Maps a generated relation model to the command-facing DTO.</summary>
    /// <param name="r">The generated model.</param>
    /// <returns>The mapped <see cref="RelationResponse"/>.</returns>
    private static RelationResponse MapRelation(Gen.RelationResponseModel r) =>
        new()
        {
            Id = r.Id ?? Guid.Empty,
            ParentId = r.Parent?.Id ?? Guid.Empty,
            ParentName = r.Parent?.Name ?? "",
            ChildId = r.Child?.Id ?? Guid.Empty,
            ChildName = r.Child?.Name ?? "",
            RelationTypeId = r.RelationType?.Id ?? Guid.Empty,
            Comment = r.Comment,
            CreateDate = r.CreateDate ?? default,
        };
}
