namespace Umbraco.Cli.Client;

/// <summary>Member group read/create/update/delete (issue #107 - parity with the MCP's member-group tools).</summary>
public interface IMemberGroupClient
{
    /// <summary>Lists member groups from the member-group tree root.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of member groups.</returns>
    Task<UmbracoResponse<PagedResponse<MemberGroupResponse>>> GetMemberGroupsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Gets a single member group by id.</summary>
    /// <param name="id">The member group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member group.</returns>
    Task<UmbracoResponse<MemberGroupResponse>> GetMemberGroupByIdAsync(
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>Creates a member group.</summary>
    /// <param name="request">The group to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created group (with its id), or a mapped failure.</returns>
    Task<UmbracoResponse<MemberGroupResponse>> CreateMemberGroupAsync(
        CreateMemberGroupRequest request,
        CancellationToken ct = default
    );

    /// <summary>Renames a member group by id.</summary>
    /// <param name="id">The member group id.</param>
    /// <param name="request">The new name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateMemberGroupAsync(
        Guid id,
        UpdateMemberGroupRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a member group by id.</summary>
    /// <param name="id">The member group id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteMemberGroupAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Read-only tag listing (issue #107).</summary>
public interface ITagClient
{
    /// <summary>Lists tags, optionally filtered by tag group and/or culture.</summary>
    /// <param name="tagGroup">Tag group to filter by; null for all groups.</param>
    /// <param name="culture">Culture to filter by; null for the invariant/default.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of tags.</returns>
    Task<UmbracoResponse<PagedResponse<TagResponse>>> GetTagsAsync(
        string? tagGroup = null,
        string? culture = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );
}

/// <summary>Read-only listing of the instance's available cultures (issue #107).</summary>
public interface ICultureClient
{
    /// <summary>Lists the cultures available on the instance.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of cultures.</returns>
    Task<UmbracoResponse<PagedResponse<CultureResponse>>> GetCulturesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );
}
