namespace Umbraco.Community.Cli.Client;

/// <summary>Member read, create, and delete.</summary>
public interface IMemberClient
{
    /// <summary>Lists members, optionally narrowed by a free-text filter.</summary>
    /// <param name="group">
    /// A free-text filter Umbraco matches against member names and emails; null lists every member.
    /// </param>
    /// <param name="skip">How many items to skip (paging).</param>
    /// <param name="take">The page size.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One page of members, or a mapped failure.</returns>
    Task<UmbracoResponse<PagedResponse<MemberResponse>>> GetMembersAsync(
        string? group = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Reads one member.</summary>
    /// <param name="id">The member id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member, or a mapped failure (404 when there is none).</returns>
    Task<UmbracoResponse<MemberResponse>> GetMemberByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a member, then reads it back so the result is what <c>get</c> shows.</summary>
    /// <param name="request">The member to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created member, or a mapped failure.</returns>
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

    /// <summary>
    /// Deletes a member. Idempotent: a member that is already gone counts as deleted, because Umbraco
    /// 17 answers some successful deletes with a 500 and the result is confirmed by reading back.
    /// </summary>
    /// <param name="id">The member id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteMemberAsync(Guid id, CancellationToken ct = default);
}
