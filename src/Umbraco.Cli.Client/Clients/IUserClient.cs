namespace Umbraco.Cli.Client;

/// <summary>Back-office user read and invitation.</summary>
public interface IUserClient
{
    Task<UmbracoResponse<PagedResponse<UserResponse>>> GetUsersAsync(
        int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<UserResponse>> GetUserByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<Empty>> InviteUserAsync(InviteUserRequest request, CancellationToken ct = default);
}
