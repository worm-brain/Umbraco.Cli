using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Backoffice users on <see cref="UmbracoManagementClient"/>: list, get, invite, create, update and
/// delete (#214, #216). Reads are labelled with their groups' aliases, names and sections, read once
/// per client. The writes that Umbraco splits over several endpoints (a create with a password, an
/// update that also disables or unlocks) are sent as a sequence here, and a failure part-way names
/// what had already been applied.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>Every user group, for labelling users; read on first use, for the client's life.</summary>
    private List<Gen.UserGroupResponseModel>? _userGroupsForLabels;

    /// <summary>
    /// Maps a generated user model onto the command-facing <see cref="UserResponse"/>. Groups carry
    /// only their ids here; <see cref="LabelUsersAsync"/> fills in the rest.
    /// </summary>
    /// <param name="user">The generated user model.</param>
    /// <returns>The mapped user (enums flattened to their names).</returns>
    private static UserResponse MapUser(Gen.UserResponseModel user) =>
        new()
        {
            Id = user.Id ?? Guid.Empty,
            Email = user.Email ?? "",
            Name = user.Name ?? "",
            UserName = user.UserName ?? "",
            State = user.State?.ToString() ?? "",
            Kind = user.Kind?.ToString() ?? "",
            IsAdmin = user.IsAdmin ?? false,
            UserGroups =
            [
                .. (user.UserGroupIds ?? [])
                    .Where(g => g.Id is not null)
                    .Select(g => new UserGroupRef { Id = g.Id!.Value }),
            ],
            LanguageIsoCode = user.LanguageIsoCode,
            DocumentStartNodes = Ids(user.DocumentStartNodeIds),
            MediaStartNodes = Ids(user.MediaStartNodeIds),
            DocumentRootAccess = user.HasDocumentRootAccess ?? false,
            MediaRootAccess = user.HasMediaRootAccess ?? false,
            FailedLoginAttempts = user.FailedLoginAttempts ?? 0,
            LastLoginDate = user.LastLoginDate,
            LastLockoutDate = user.LastLockoutDate,
            LastPasswordChangeDate = user.LastPasswordChangeDate,
            CreateDate = user.CreateDate ?? default,
            UpdateDate = user.UpdateDate ?? default,
        };

    /// <summary>The ids of a list of references, skipping any without one.</summary>
    /// <param name="refs">The references, or null.</param>
    /// <returns>The ids.</returns>
    private static IReadOnlyList<Guid> Ids(IEnumerable<Gen.ReferenceByIdModel>? refs) =>
        [.. (refs ?? []).Where(r => r.Id is not null).Select(r => r.Id!.Value)];

    /// <summary>
    /// Fills in each user's group aliases and names and the sections those groups grant (#216). The
    /// group list is read once for the whole batch; if it cannot be read (a 403 for an API user
    /// without the Users section, say) the ids stay and the labels and sections are left empty,
    /// rather than failing the read.
    /// </summary>
    /// <param name="users">The mapped users.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The users with their labels, in the same order.</returns>
    private async Task<List<UserResponse>> LabelUsersAsync(
        IReadOnlyList<UserResponse> users,
        CancellationToken ct
    )
    {
        List<Gen.UserGroupResponseModel>? groups = null;
        if (users.Any(u => u.UserGroups.Count > 0))
        {
            try
            {
                groups = _userGroupsForLabels ??= await ReadAllUserGroupsAsync(ct);
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException)
            {
                // The ids are still right; only the labels are missing.
            }
        }

        return [.. users.Select(u => Label(u, groups))];
    }

    /// <summary>One user with the labels from <paramref name="groups"/>; see <see cref="LabelUsersAsync"/>.</summary>
    /// <param name="user">The mapped user.</param>
    /// <param name="groups">Every user group, or null when they could not be read.</param>
    /// <returns>The labelled user.</returns>
    private static UserResponse Label(UserResponse user, List<Gen.UserGroupResponseModel>? groups)
    {
        var mine = user
            .UserGroups.Select(g => (Ref: g, Group: groups?.FirstOrDefault(x => x.Id == g.Id)))
            .ToList();
        return user with
        {
            UserGroups =
            [
                .. mine.Select(m => m.Ref with { Alias = m.Group?.Alias, Name = m.Group?.Name }),
            ],
            Sections =
            [
                .. mine.SelectMany(m => m.Group?.Sections ?? []).Distinct(StringComparer.Ordinal),
            ],
        };
    }

    /// <summary>Lists users via <c>GET user?skip=&amp;take=</c> (generated client, #79).</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of users mapped to <see cref="UserResponse"/>, with their group labels.</returns>
    public Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.User.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<UserResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = await LabelUsersAsync([.. (paged?.Items ?? []).Select(MapUser)], ct),
                };
            }
        );

    /// <summary>Gets a single user by id via <c>GET user/{id}</c> (generated client, #79).</summary>
    /// <param name="id">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user mapped to <see cref="UserResponse"/>, with their group labels.</returns>
    public Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(
        Guid id,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var user = await _api
                    .Umbraco.Management.Api.V1.User[id]
                    .GetAsync(cancellationToken: ct);
                return user is null
                    ? new UserResponse { Id = id }
                    : (await LabelUsersAsync([MapUser(user)], ct))[0];
            }
        );

    /// <summary>
    /// Invites a user via <c>POST user/invite</c> (generated client). The endpoint sends the
    /// invitation email and returns no body, so an empty success response is returned.
    /// </summary>
    /// <param name="request">
    /// The invite details. Groups can be given as ids (<see cref="InviteUserRequest.UserGroupIds"/>)
    /// or as alias, name or id references (<see cref="InviteUserRequest.UserGroups"/>); both are sent.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure (a 404 when a group reference matches no group).</returns>
    public Task<UmbracoResponse<Empty>> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var groupIds = request
                    .UserGroupIds.Select(g => g.Id)
                    .Concat(await ResolveUserGroupIdsAsync(request.UserGroups, ct));
                var body = new Gen.InviteUserRequestModel
                {
                    Email = request.Email,
                    Name = request.Name,
                    // Umbraco refuses an invite with no userName, and by default one whose
                    // userName differs from the email (#215), so the email is the default.
                    UserName = request.UserName ?? request.Email,
                    Message = request.Message,
                    UserGroupIds = groupIds
                        .Select(id => new Gen.ReferenceByIdModel { Id = id })
                        .ToList(),
                };
                await _api.Umbraco.Management.Api.V1.User.Invite.PostAsync(
                    body,
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <summary>
    /// Creates a user via <c>POST user</c> and, when a password is given, sets it with
    /// <c>POST user/{id}/change-password</c> (#214) - the create endpoint takes none, and unlike an
    /// invite it sends no email, so it works on a site without SMTP.
    /// <para>
    /// If the password step fails (most often Umbraco's password policy), the new user is deleted
    /// again, so the create either happens completely or not at all and can simply be retried. If
    /// that delete fails too, the error names the user id that was left behind.
    /// </para>
    /// </summary>
    /// <param name="request">The user to create; groups by alias, name or id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new user's id, or a mapped failure.</returns>
    public async Task<UmbracoResponse<Guid>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken ct = default
    )
    {
        // Client-generated id (Umbraco 14+ accepts a supplied GUID), so the password call and the
        // read-back need no Location header parsing.
        var id = request.Id ?? Guid.NewGuid();
        var created = await GuardedApiAsync(
            ct,
            async () =>
            {
                var groups = await ResolveUserGroupIdsAsync(request.UserGroups, ct);
                await _api.Umbraco.Management.Api.V1.User.PostAsync(
                    new Gen.CreateUserRequestModel
                    {
                        Id = id,
                        Email = request.Email,
                        Name = request.Name,
                        // As for invites (#215): Umbraco requires a userName, by default the email.
                        UserName = request.UserName ?? request.Email,
                        Kind = Gen.UserKindModel.Default,
                        UserGroupIds =
                        [
                            .. groups.Select(g => new Gen.ReferenceByIdModel { Id = g }),
                        ],
                    },
                    cancellationToken: ct
                );
                return id;
            }
        );
        if (!created.IsSuccess || string.IsNullOrEmpty(request.Password))
            return created;

        var password = await ChangeUserPasswordAsync(id, request.Password, ct);
        if (password.IsSuccess)
            return created;

        // Roll the create back, so a rejected password never leaves a user nobody can sign in as.
        var removed = await DeleteUserAsync(id, ct);
        var message = removed.IsSuccess
            ? $"Setting the password failed, so the new user was deleted again: {password.ErrorMessage}"
            : $"User {id} was created, but setting its password failed ({password.ErrorMessage}) "
                + $"and deleting it again failed too ({removed.ErrorMessage}). Set a password with "
                + $"'umbraco user update {id} --new-password <secret>' or delete it with "
                + $"'umbraco user delete {id}'.";
        return UmbracoResponse<Guid>.FailureFrom(password) with { ErrorMessage = message };
    }

    /// <summary>
    /// Updates a user (#216). The profile fields are merged over the current user and written with
    /// <c>PUT user/{id}</c>, which replaces the whole profile, so an omitted field keeps its value;
    /// the PUT is skipped when no profile field is given. Then, each only when asked for: the
    /// password (<c>change-password</c>), the enabled state (<c>disable</c> / <c>enable</c>) and the
    /// lockout (<c>unlock</c>). The steps run in that order and stop at the first failure, whose
    /// message says which steps had already been applied.
    /// </summary>
    /// <param name="id">The user id.</param>
    /// <param name="request">What to change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or the first failure.</returns>
    public async Task<UmbracoResponse<Empty>> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken ct = default
    )
    {
        var steps = new List<(string Name, Func<Task<UmbracoResponse<Empty>>> Run)>();
        if (request.ChangesProfile)
            steps.Add(("the profile", () => UpdateUserProfileAsync(id, request, ct)));
        if (!string.IsNullOrEmpty(request.NewPassword))
            steps.Add(("the password", () => ChangeUserPasswordAsync(id, request.NewPassword, ct)));
        if (request.Disabled is { } disabled)
            steps.Add(
                (disabled ? "disabling" : "enabling", () => SetUserDisabledAsync(id, disabled, ct))
            );
        if (request.Unlock)
            steps.Add(("the unlock", () => UnlockUserAsync(id, ct)));

        var done = new List<string>();
        foreach (var (name, run) in steps)
        {
            var result = await run();
            if (!result.IsSuccess)
                return done.Count == 0
                    ? result
                    : result with
                    {
                        ErrorMessage =
                            $"Applied {string.Join(" and ", done)}, but {name} failed: {result.ErrorMessage}",
                    };
            done.Add(name);
        }
        return UmbracoResponse<Empty>.Success(Empty.Value);
    }

    /// <summary>
    /// Merges the profile fields over the current user and PUTs them. Groups given replace the
    /// user's groups (a group list is the membership, as for members); start nodes and root access
    /// are carried through unchanged.
    /// </summary>
    /// <param name="id">The user id.</param>
    /// <param name="request">The fields to change; null keeps the current value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or a mapped failure (a 404 for an unknown user or group).</returns>
    private Task<UmbracoResponse<Empty>> UpdateUserProfileAsync(
        Guid id,
        UpdateUserRequest request,
        CancellationToken ct
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var user = _api.Umbraco.Management.Api.V1.User[id];
                var current =
                    await user.GetAsync(cancellationToken: ct)
                    ?? throw NotFound($"No user found with id '{id}'.");
                // Resolved before the write, so a mistyped group changes nothing.
                var groups = request.UserGroups is { Count: > 0 } given
                    ?
                    [
                        .. (await ResolveUserGroupIdsAsync(given, ct)).Select(
                            g => new Gen.ReferenceByIdModel { Id = g }
                        ),
                    ]
                    : current.UserGroupIds ?? [];
                await user.PutAsync(
                    new Gen.UpdateUserRequestModel
                    {
                        Email = request.Email ?? current.Email,
                        UserName = request.UserName ?? current.UserName,
                        Name = request.Name ?? current.Name,
                        UserGroupIds = groups,
                        LanguageIsoCode = request.LanguageIsoCode ?? current.LanguageIsoCode,
                        DocumentStartNodeIds = current.DocumentStartNodeIds ?? [],
                        MediaStartNodeIds = current.MediaStartNodeIds ?? [],
                        HasDocumentRootAccess = current.HasDocumentRootAccess ?? false,
                        HasMediaRootAccess = current.HasMediaRootAccess ?? false,
                    },
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <summary>Sets a user's password via <c>POST user/{id}/change-password</c> (an admin change).</summary>
    /// <param name="id">The user id.</param>
    /// <param name="password">The new password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or a mapped failure (a 400 when the password breaks the policy).</returns>
    private Task<UmbracoResponse<Empty>> ChangeUserPasswordAsync(
        Guid id,
        string password,
        CancellationToken ct
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.User[id]
                    .ChangePassword.PostAsync(
                        new Gen.ChangePasswordUserRequestModel { NewPassword = password },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <summary>Disables or enables a user via <c>POST user/disable</c> or <c>POST user/enable</c>.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="disabled">True to disable, false to enable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or a mapped failure.</returns>
    private Task<UmbracoResponse<Empty>> SetUserDisabledAsync(
        Guid id,
        bool disabled,
        CancellationToken ct
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                List<Gen.ReferenceByIdModel> ids = [new() { Id = id }];
                if (disabled)
                    await _api.Umbraco.Management.Api.V1.User.Disable.PostAsync(
                        new Gen.DisableUserRequestModel { UserIds = ids },
                        cancellationToken: ct
                    );
                else
                    await _api.Umbraco.Management.Api.V1.User.Enable.PostAsync(
                        new Gen.EnableUserRequestModel { UserIds = ids },
                        cancellationToken: ct
                    );
                return Empty.Value;
            }
        );

    /// <summary>Clears a user's lockout via <c>POST user/unlock</c>.</summary>
    /// <param name="id">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success, or a mapped failure.</returns>
    private Task<UmbracoResponse<Empty>> UnlockUserAsync(Guid id, CancellationToken ct) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.User.Unlock.PostAsync(
                    new Gen.UnlockUsersRequestModel { UserIds = [new() { Id = id }] },
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteUserAsync(Guid id, CancellationToken ct = default) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.User[id].DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteUsersAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.User.DeleteAsync(
                    new Gen.DeleteUsersRequestModel
                    {
                        UserIds = [.. ids.Select(i => new Gen.ReferenceByIdModel { Id = i })],
                    },
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );
}
