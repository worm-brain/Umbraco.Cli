namespace Umbraco.Cli.Client;

/// <summary>Member read, create, and delete.</summary>
public interface IMemberClient
{
    Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null, int skip = 0, int take = 20, CancellationToken ct = default);

    Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(Guid id, CancellationToken ct = default);

    Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(CreateMemberRequest request, CancellationToken ct = default);

    Task<UmbracoResponse<Empty>> DeleteMemberAsync(Guid id, CancellationToken ct = default);
}
