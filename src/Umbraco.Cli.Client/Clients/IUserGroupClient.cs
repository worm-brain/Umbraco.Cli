namespace Umbraco.Cli.Client;

/// <summary>
/// User group read/create/update/delete plus bulk delete and user membership (issue #109 -
/// parity with the MCP's user-group tools), including granular per-document permissions (#111).
/// </summary>
public interface IUserGroupClient
{
    /// <summary>Lists user groups.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of user groups.</returns>
    Task<UmbracoResponse<PagedResponse<UserGroupResponse>>> GetUserGroupsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets a single user group by id.</summary>
    /// <param name="id">The user group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user group.</returns>
    Task<UmbracoResponse<UserGroupResponse>> GetUserGroupByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a user group.</summary>
    /// <param name="request">The group to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The created group as saved (read back, so server-set fields such as isDeletable match
    /// <c>get</c>), or a mapped failure.
    /// </returns>
    Task<UmbracoResponse<UserGroupResponse>> CreateUserGroupAsync(
        CreateUserGroupRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a user group by id.</summary>
    /// <param name="id">The user group id.</param>
    /// <param name="request">The new group state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateUserGroupAsync(
        Guid id,
        UpdateUserGroupRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a single user group by id.</summary>
    /// <param name="id">The user group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteUserGroupAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Counts the users in a user group (<c>GET filter/user?userGroupIds</c>), who would lose the
    /// access it grants if it were deleted (#269).
    /// </summary>
    /// <param name="userGroupId">The user group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of users in the group, or a mapped failure.</returns>
    Task<UmbracoResponse<int>> CountUsersInGroupAsync(
        Guid userGroupId,
        CancellationToken ct = default
    );

    /// <summary>Deletes several user groups in one call (collection-level bulk delete).</summary>
    /// <param name="ids">The user group ids to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteUserGroupsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    );

    /// <summary>Adds users to a user group.</summary>
    /// <param name="id">The user group id.</param>
    /// <param name="userIds">The ids of the users to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> AddUsersToGroupAsync(
        Guid id,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default
    );

    /// <summary>Removes users from a user group.</summary>
    /// <param name="id">The user group id.</param>
    /// <param name="userIds">The ids of the users to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> RemoveUsersFromGroupAsync(
        Guid id,
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default
    );
}

/// <summary>
/// User-data key/value read/create/update/delete (issue #109). User data is scoped to the
/// authenticated user; update is a collection-level PUT that carries the target key in the body.
/// </summary>
public interface IUserDataClient
{
    /// <summary>Lists user-data entries, optionally filtered by group and/or identifier.</summary>
    /// <param name="group">Group to filter by; null for all groups.</param>
    /// <param name="identifier">Identifier to filter by; null for all identifiers.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of user-data entries.</returns>
    Task<UmbracoResponse<PagedResponse<UserDataResponse>>> GetUserDataAsync(
        string? group = null,
        string? identifier = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets a single user-data entry by key.</summary>
    /// <param name="id">The entry key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user-data entry, or a mapped failure (a 404 naming the key when there is none).</returns>
    Task<UmbracoResponse<UserDataResponse>> GetUserDataByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a user-data entry.</summary>
    /// <param name="request">The entry to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created entry as saved (echoed if the read-back fails), or a mapped failure.</returns>
    Task<UmbracoResponse<UserDataResponse>> CreateUserDataAsync(
        CreateUserDataRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates a user-data entry (collection-level PUT keyed by the body).</summary>
    /// <param name="request">The entry state, including the key to update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure (a 404 naming the key when there is none).</returns>
    Task<UmbracoResponse<Empty>> UpdateUserDataAsync(
        UpdateUserDataRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a user-data entry by key.</summary>
    /// <param name="id">The entry key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure (a 404 naming the key when there is none).</returns>
    Task<UmbracoResponse<Empty>> DeleteUserDataAsync(Guid id, CancellationToken ct = default);
}
