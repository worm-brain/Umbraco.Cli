namespace Umbraco.Cli.Client;

/// <summary>Member read, create, and delete.</summary>
public interface IMemberClient
{
    Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<MemberResponse>> CreateMemberAsync(
        CreateMemberRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Updates a member by id (issue #59). Only the fields set on <paramref name="request"/>
    /// are changed; the client reads the current member and merges them so groups, property
    /// values and password are preserved across the underlying full-replace PUT.
    /// </summary>
    /// <param name="id">The member id.</param>
    /// <param name="request">The partial changes to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated member, or a mapped failure.</returns>
    Task<UmbracoResponse<MemberResponse>> UpdateMemberAsync(
        Guid id,
        UpdateMemberRequest request,
        CancellationToken ct = default
    );

    Task<UmbracoResponse<Empty>> DeleteMemberAsync(Guid id, CancellationToken ct = default);
}
