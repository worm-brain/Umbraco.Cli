namespace Umbraco.Cli.Client;

/// <summary>Backoffice users: read, invite, create, update and delete (#214, #216).</summary>
public interface IUserClient
{
    /// <summary>Lists a page of users, each with its groups' aliases, names and sections.</summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page, or a mapped failure.</returns>
    Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Gets one user by id, with its groups' aliases, names and sections.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user, or a mapped failure.</returns>
    Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Invites a user by email (the site needs SMTP).</summary>
    /// <param name="request">The invitation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a user without sending an email, then sets its password if one is given. A failed
    /// password step deletes the new user again, so the create is all or nothing.
    /// </summary>
    /// <param name="request">The user to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new user's id, or a mapped failure.</returns>
    Task<UmbracoResponse<Guid>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Updates a user: merges the given profile fields over the current ones, then sets the
    /// password, enabled state and lockout when asked, stopping at the first failure.
    /// </summary>
    /// <param name="id">The user id.</param>
    /// <param name="request">What to change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or the first failure, naming the steps already applied.</returns>
    Task<UmbracoResponse<Empty>> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes one user via <c>DELETE user/{id}</c>.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An empty success, or a mapped failure; a refusal because the user has signed in names the
    /// user and how to disable them instead.
    /// </returns>
    Task<UmbracoResponse<Empty>> DeleteUserAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes several users in one call via <c>DELETE user</c> with their ids in the body.</summary>
    /// <param name="ids">The user ids.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An empty success, or a mapped failure; a refusal because a user has signed in names which
    /// of the users have, and how to disable them instead.
    /// </returns>
    Task<UmbracoResponse<Empty>> DeleteUsersAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    );
}
