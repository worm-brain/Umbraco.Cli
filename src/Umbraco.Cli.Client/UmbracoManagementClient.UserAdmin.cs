using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// User-administration resources on <see cref="UmbracoManagementClient"/> - user groups (full CRUD,
/// bulk delete, and user membership) and user data (key/value CRUD) (issue #109). Kept in their own
/// partial so the main client file stays focused. Granular per-node user-group permissions are a
/// deferred follow-up: creates and updates send an empty <c>permissions</c> array.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    // ── User groups ────────────────────────────────────────────────────────────

    /// <summary>How many user groups to read per page while resolving references.</summary>
    private const int UserGroupPageSize = 100;

    /// <summary>
    /// Resolves user-group references - a GUID, an alias or a name - to ids, in the order given
    /// (#215). A GUID is taken as an id as it stands; anything else is matched against every
    /// group's alias and then its name, ignoring case. The group list is only read when there is
    /// something to look up.
    /// </summary>
    /// <param name="references">The references.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids.</returns>
    /// <exception cref="ApiException">A reference matched no group (a 404).</exception>
    private async Task<IReadOnlyList<Guid>> ResolveUserGroupIdsAsync(
        IReadOnlyList<string> references,
        CancellationToken ct
    )
    {
        var ids = new List<Guid>();
        foreach (var reference in references)
            ids.Add(await IdOfAsync(EntityKind.UserGroup, reference, ct));
        return ids;
    }

    /// <summary>Reads every user group, page by page.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All the groups.</returns>
    private async Task<List<Gen.UserGroupResponseModel>> ReadAllUserGroupsAsync(
        CancellationToken ct
    )
    {
        var all = new List<Gen.UserGroupResponseModel>();
        while (true)
        {
            var page = await _api.Umbraco.Management.Api.V1.UserGroup.GetAsync(
                c =>
                {
                    c.QueryParameters.Skip = all.Count;
                    c.QueryParameters.Take = UserGroupPageSize;
                },
                ct
            );
            var items = page?.Items ?? [];
            all.AddRange(items);
            // Stop on a short page as well as on the total, so a server that over-reports its
            // total cannot loop forever.
            if (items.Count < UserGroupPageSize || all.Count >= (page?.Total ?? 0))
                return all;
        }
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<UserGroupResponse>>> GetUserGroupsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.UserGroup.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<UserGroupResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapUserGroup).ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<UserGroupResponse>> GetUserGroupByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var g = await _api
                    .Umbraco.Management.Api.V1.UserGroup[id]
                    .GetAsync(cancellationToken: ct);
                return g is null ? new UserGroupResponse { Id = id } : MapUserGroup(g);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<UserGroupResponse>> CreateUserGroupAsync(
        CreateUserGroupRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Client-generated id (Umbraco 14+ accepts a supplied GUID); the 201 body is empty,
                // so echo the created group without a follow-up read.
                var id = request.Id ?? Guid.NewGuid();
                await _api.Umbraco.Management.Api.V1.UserGroup.PostAsync(
                    new Gen.CreateUserGroupRequestModel
                    {
                        Id = id,
                        Alias = request.Alias,
                        Name = request.Name,
                        Icon = request.Icon,
                        Description = request.Description,
                        Sections = request.Sections.ToList(),
                        Languages = request.Languages.ToList(),
                        FallbackPermissions = request.FallbackPermissions.ToList(),
                        HasAccessToAllLanguages = request.HasAccessToAllLanguages,
                        DocumentRootAccess = request.DocumentRootAccess,
                        MediaRootAccess = request.MediaRootAccess,
                        DocumentStartNode = Ref(request.DocumentStartNode),
                        MediaStartNode = Ref(request.MediaStartNode),
                        // Granular per-node permissions are a deferred follow-up.
                        Permissions = [],
                    },
                    cancellationToken: ct
                );
                return new UserGroupResponse
                {
                    Id = id,
                    Alias = request.Alias,
                    Name = request.Name,
                    Icon = request.Icon,
                    Description = request.Description,
                    Sections = request.Sections,
                    Languages = request.Languages,
                    FallbackPermissions = request.FallbackPermissions,
                    HasAccessToAllLanguages = request.HasAccessToAllLanguages,
                    DocumentRootAccess = request.DocumentRootAccess,
                    MediaRootAccess = request.MediaRootAccess,
                    DocumentStartNode = request.DocumentStartNode,
                    MediaStartNode = request.MediaStartNode,
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateUserGroupAsync(
        Guid id,
        UpdateUserGroupRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.UserGroup[id]
                    .PutAsync(
                        new Gen.UpdateUserGroupRequestModel
                        {
                            Alias = request.Alias,
                            Name = request.Name,
                            Icon = request.Icon,
                            Description = request.Description,
                            Sections = request.Sections.ToList(),
                            Languages = request.Languages.ToList(),
                            FallbackPermissions = request.FallbackPermissions.ToList(),
                            HasAccessToAllLanguages = request.HasAccessToAllLanguages,
                            DocumentRootAccess = request.DocumentRootAccess,
                            MediaRootAccess = request.MediaRootAccess,
                            DocumentStartNode = Ref(request.DocumentStartNode),
                            MediaStartNode = Ref(request.MediaStartNode),
                            // Granular per-node permissions are a deferred follow-up.
                            Permissions = [],
                        },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteUserGroupAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.UserGroup[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteUserGroupsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.UserGroup.DeleteAsync(
                    new Gen.DeleteUserGroupsRequestModel
                    {
                        UserGroupIds = ids.Select(i => new Gen.ReferenceByIdModel { Id = i })
                            .ToList(),
                    },
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> AddUsersToGroupAsync(
        Guid id,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.UserGroup[id]
                    .Users.PostAsync(
                        userIds.Select(u => new Gen.ReferenceByIdModel { Id = u }).ToList(),
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> RemoveUsersFromGroupAsync(
        Guid id,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.UserGroup[id]
                    .Users.DeleteAsync(
                        userIds.Select(u => new Gen.ReferenceByIdModel { Id = u }).ToList(),
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <summary>A node reference for a start node, or null for none.</summary>
    private static Gen.ReferenceByIdModel? Ref(Guid? id) =>
        id is { } value ? new Gen.ReferenceByIdModel { Id = value } : null;

    /// <summary>Maps a generated user-group response model to the command-facing DTO.</summary>
    /// <param name="g">The generated model.</param>
    /// <returns>The mapped <see cref="UserGroupResponse"/>.</returns>
    private static UserGroupResponse MapUserGroup(Gen.UserGroupResponseModel g) =>
        new()
        {
            Id = g.Id ?? Guid.Empty,
            Alias = g.Alias ?? "",
            Name = g.Name ?? "",
            Icon = g.Icon,
            Description = g.Description,
            Sections = g.Sections ?? [],
            Languages = g.Languages ?? [],
            FallbackPermissions = g.FallbackPermissions ?? [],
            HasAccessToAllLanguages = g.HasAccessToAllLanguages ?? false,
            DocumentRootAccess = g.DocumentRootAccess ?? false,
            MediaRootAccess = g.MediaRootAccess ?? false,
            DocumentStartNode = g.DocumentStartNode?.Id,
            MediaStartNode = g.MediaStartNode?.Id,
            IsDeletable = g.IsDeletable ?? false,
            AliasCanBeChanged = g.AliasCanBeChanged ?? false,
        };

    // ── User data ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<UserDataResponse>>> GetUserDataAsync(
        string? group = null,
        string? identifier = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.UserData.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                        if (!string.IsNullOrEmpty(group))
                            c.QueryParameters.Groups = [group];
                        if (!string.IsNullOrEmpty(identifier))
                            c.QueryParameters.Identifiers = [identifier];
                    },
                    ct
                );
                return new PagedResponse<UserDataResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(d =>
                            MapUserData(d.Key ?? Guid.Empty, d.Group, d.Identifier, d.Value)
                        )
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<UserDataResponse>> GetUserDataByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var d = await _api
                    .Umbraco.Management.Api.V1.UserData[id]
                    .GetAsync(cancellationToken: ct);
                // The item body carries group/identifier/value but not the key; echo the requested id.
                return MapUserData(id, d?.Group, d?.Identifier, d?.Value);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<UserDataResponse>> CreateUserDataAsync(
        CreateUserDataRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // Client-generated key (the 201 body is empty), echoed back to the caller.
                var key = request.Key ?? Guid.NewGuid();
                await _api.Umbraco.Management.Api.V1.UserData.PostAsync(
                    new Gen.CreateUserDataRequestModel
                    {
                        Key = key,
                        Group = request.Group,
                        Identifier = request.Identifier,
                        Value = request.Value,
                    },
                    cancellationToken: ct
                );
                return MapUserData(key, request.Group, request.Identifier, request.Value);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateUserDataAsync(
        UpdateUserDataRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.UserData.PutAsync(
                    new Gen.UpdateUserDataRequestModel
                    {
                        Key = request.Key,
                        Group = request.Group,
                        Identifier = request.Identifier,
                        Value = request.Value,
                    },
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteUserDataAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.UserData[id]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <summary>
    /// Projects a user-data entry into the command-facing DTO. Takes the fields rather than a
    /// generated model because the two read paths return different generated shapes (the list's
    /// <c>UserDataResponseModel</c> carries the key; the item's <c>UserDataModel</c> does not, so the
    /// caller supplies the requested id).
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <param name="group">The group, or null.</param>
    /// <param name="identifier">The identifier, or null.</param>
    /// <param name="value">The value, or null.</param>
    /// <returns>The mapped <see cref="UserDataResponse"/>.</returns>
    private static UserDataResponse MapUserData(
        Guid key,
        string? group,
        string? identifier,
        string? value
    ) =>
        new()
        {
            Key = key,
            Group = group ?? "",
            Identifier = identifier ?? "",
            Value = value ?? "",
        };
}
