using System.Runtime.ExceptionServices;
using Microsoft.Kiota.Abstractions;

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
                // The shared batch reader (#432): /document-type/batch on 17.3+, and one read per
                // id after its 404 on 17.0-17.2, which have no batch endpoints.
                //
                // The reader leaves a failed by-id read out, which for a guard would read as
                // "does not use the template" and let the delete strip it from that type. So the
                // first failure other than a 404 is kept and rethrown below, failing the guard
                // with that read's own status, as a failed batch request does. A 404 is a type
                // deleted since the tree was walked: skipped, as the batch leaves it out.
                ApiException? readFailure = null;
                var types = await ReadInBatchesAsync(
                    ids.Data!,
                    async (chunk, c) =>
                        (
                            await _api.Umbraco.Management.Api.V1.DocumentType.Batch.GetAsync(
                                q => q.QueryParameters.Id = [.. chunk.Select(i => (Guid?)i)],
                                c
                            )
                        )?.Items,
                    async (id, c) =>
                    {
                        try
                        {
                            return await _api
                                .Umbraco.Management.Api.V1.DocumentType[id]
                                .GetAsync(cancellationToken: c);
                        }
                        catch (ApiException e) when (e.ResponseStatusCode != 404)
                        {
                            // Reads run a few at a time, so only the first failure is kept.
                            Interlocked.CompareExchange(ref readFailure, e, null);
                            throw;
                        }
                    },
                    type => type.Id,
                    ct
                );
                if (readFailure is not null)
                    ExceptionDispatchInfo.Throw(readFailure);

                var usage = new Dictionary<Guid, List<TemplateUser>>();
                foreach (var type in types.Values)
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
                return usage.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<TemplateUser>)kv.Value
                );
            }
        );
    }
}
