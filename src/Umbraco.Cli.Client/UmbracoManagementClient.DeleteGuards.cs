namespace Umbraco.Cli.Client;

/// <summary>
/// The reads behind the cascading-delete guards (#269). Umbraco deletes a template, member group or
/// user group without saying what depended on it, so the CLI asks first and refuses without
/// <c>--force</c> when something does (docs/conventions.md 5.2).
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>How many document types one <c>document-type/batch</c> request asks for.</summary>
    private const int DocumentTypeBatch = 40;

    /// <inheritdoc />
    public Task<UmbracoResponse<int>> CountMembersInGroupAsync(
        Guid memberGroupId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // filter/member filters by group NAME (#184), so read the group first.
                var group = await _api
                    .Umbraco.Management.Api.V1.MemberGroup[memberGroupId]
                    .GetAsync(cancellationToken: ct);
                var paged = await _api.Umbraco.Management.Api.V1.Filter.Member.GetAsync(
                    c =>
                    {
                        c.QueryParameters.MemberGroupName = group?.Name;
                        c.QueryParameters.Take = 1;
                    },
                    ct
                );
                return (int)(paged?.Total ?? 0);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<int>> CountUsersInGroupAsync(
        Guid userGroupId,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // One item is enough: only the page's total is read.
                var paged = await _api.Umbraco.Management.Api.V1.Filter.User.GetAsync(
                    c =>
                    {
                        c.QueryParameters.UserGroupIds = [userGroupId];
                        c.QueryParameters.Take = 1;
                    },
                    ct
                );
                return (int)(paged?.Total ?? 0);
            }
        );

    /// <inheritdoc />
    public async Task<UmbracoResponse<IReadOnlyList<string>>> GetDocumentTypesUsingTemplateAsync(
        Guid templateId,
        CancellationToken ct = default
    )
    {
        // No endpoint says what uses a template, so read every document type (in batches) and
        // keep those that allow it or default to it.
        var ids = await GetDocumentTypeIdsAsync(ct);
        if (!ids.IsSuccess)
            return UmbracoResponse<IReadOnlyList<string>>.FailureFrom(ids);

        return await GuardedApiAsync<IReadOnlyList<string>>(
            ct,
            async () =>
            {
                var users = new List<string>();
                foreach (var chunk in ids.Data!.Chunk(DocumentTypeBatch))
                {
                    var batch = await _api.Umbraco.Management.Api.V1.DocumentType.Batch.GetAsync(
                        c => c.QueryParameters.Id = [.. chunk.Select(i => (Guid?)i)],
                        ct
                    );
                    users.AddRange(
                        (batch?.Items ?? [])
                            .Where(t =>
                                t.DefaultTemplate?.Id == templateId
                                || (t.AllowedTemplates ?? []).Any(a => a.Id == templateId)
                            )
                            .Select(t => t.Name ?? t.Alias ?? t.Id?.ToString() ?? "")
                    );
                }
                return users;
            }
        );
    }
}
