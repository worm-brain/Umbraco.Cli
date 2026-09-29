namespace Umbraco.Cli.Client;

/// <summary>
/// The reads behind the cascading-delete guards (#269). Umbraco deletes a template, member group or
/// user group without saying what depended on it, so the CLI asks first and refuses without
/// <c>--force</c> when something does (docs/conventions.md 5.2).
/// </summary>
public sealed partial class UmbracoManagementClient
{
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
    public async Task<
        UmbracoResponse<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>
    > GetTemplateUsageAsync(CancellationToken ct = default)
    {
        // No endpoint says what uses a template, so read every document type once (in batches)
        // and index them by each template they allow or default to. One read answers for every
        // template a prune deletes.
        var ids = await GetDocumentTypeIdsAsync(ct);
        if (!ids.IsSuccess)
            return UmbracoResponse<
                IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>
            >.FailureFrom(ids);

        return await GuardedApiAsync<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>(
            ct,
            async () =>
            {
                var usage = new Dictionary<Guid, List<TemplateUser>>();
                foreach (var chunk in ids.Data!.Chunk(TypeBatchSize))
                {
                    var batch = await _api.Umbraco.Management.Api.V1.DocumentType.Batch.GetAsync(
                        c => c.QueryParameters.Id = [.. chunk.Select(i => (Guid?)i)],
                        ct
                    );
                    foreach (var type in batch?.Items ?? [])
                    {
                        var user = new TemplateUser(
                            type.Id ?? Guid.Empty,
                            type.Name ?? type.Alias ?? type.Id?.ToString() ?? ""
                        );
                        var templates = (type.AllowedTemplates ?? [])
                            .Select(t => t.Id)
                            .Append(type.DefaultTemplate?.Id)
                            .OfType<Guid>()
                            .Distinct();
                        foreach (var template in templates)
                        {
                            if (!usage.TryGetValue(template, out var list))
                                usage[template] = list = [];
                            list.Add(user);
                        }
                    }
                }
                return usage.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<TemplateUser>)kv.Value
                );
            }
        );
    }
}
