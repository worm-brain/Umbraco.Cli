namespace Umbraco.Cli.Client;

/// <summary>Member type read, create, update, and delete (issue #56 — parity with the MCP's member-type tools).</summary>
public interface IMemberTypeClient
{
    /// <summary>Lists member types from the member-type tree root.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of member types mapped to <see cref="MemberTypeResponse"/>.</returns>
    Task<UmbracoResponse<PagedResponse<MemberTypeResponse>>> GetMemberTypesAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Gets a single member type by id, including its alias and description.</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member type mapped to <see cref="MemberTypeResponse"/>.</returns>
    Task<UmbracoResponse<MemberTypeResponse>> GetMemberTypeByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a member type.</summary>
    /// <param name="request">The member type to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created member type (with the generated id), or a mapped failure.</returns>
    Task<UmbracoResponse<MemberTypeResponse>> CreateMemberTypeAsync(
        CreateMemberTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Updates a member type by id. Only the supplied scalar fields change; the type's
    /// properties, containers, compositions and varies-by flags are preserved by a raw-JSON
    /// read-merge (the typed update model would drop them).
    /// </summary>
    /// <param name="id">The member type id.</param>
    /// <param name="request">The fields to change; null fields keep their current value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateMemberTypeAsync(
        Guid id,
        UpdateMemberTypeRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a member type by id.</summary>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteMemberTypeAsync(Guid id, CancellationToken ct = default);
}
