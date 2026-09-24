using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Users;

/// <summary>
/// Resolves user-group references given on the command line - a GUID, an alias or a name - to
/// group ids (#215).
/// <para>
/// A small, local resolver for <c>users invite --group</c>. The shared <c>&lt;id|alias&gt;</c>
/// resolver planned for #250 Phase 3 is expected to absorb it.
/// </para>
/// </summary>
public static class UserGroupReferences
{
    /// <summary>How many groups to read per page while resolving.</summary>
    private const int PageSize = 100;

    /// <summary>
    /// Resolves each reference to a group id. A GUID is taken as an id as it stands; anything
    /// else is matched against every group's alias and then its name, ignoring case.
    /// </summary>
    /// <param name="client">The client to read the user groups with.</param>
    /// <param name="references">The references, in the order given.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The ids in the order given, or a failure naming the first reference that matched no
    /// group (or the failed read of the group list).
    /// </returns>
    public static async Task<UmbracoResponse<IReadOnlyList<Guid>>> ResolveAsync(
        IUserGroupClient client,
        IReadOnlyList<string> references,
        CancellationToken ct
    )
    {
        // Only read the group list when there is something to look up by alias or name.
        List<UserGroupResponse>? groups = null;
        var ids = new List<Guid>();
        foreach (var reference in references)
        {
            if (Guid.TryParse(reference, out var id))
            {
                ids.Add(id);
                continue;
            }

            if (groups is null)
            {
                var read = await ReadAllAsync(client, ct);
                if (!read.IsSuccess)
                    return UmbracoResponse<IReadOnlyList<Guid>>.FailureFrom(read);
                groups = read.Data!;
            }

            // Alias first, because it is the group's stable key; the name is a convenience.
            var match =
                groups.FirstOrDefault(g =>
                    string.Equals(g.Alias, reference, StringComparison.OrdinalIgnoreCase)
                )
                ?? groups.FirstOrDefault(g =>
                    string.Equals(g.Name, reference, StringComparison.OrdinalIgnoreCase)
                );
            if (match is null)
                return UmbracoResponse<IReadOnlyList<Guid>>.Failure(
                    404,
                    $"No user group found with alias or name '{reference}'. "
                        + "Run 'umbraco user-groups list' to see the groups."
                );
            ids.Add(match.Id);
        }
        return UmbracoResponse<IReadOnlyList<Guid>>.Success(ids);
    }

    /// <summary>Reads every user group, page by page.</summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All the groups, or the failed page read.</returns>
    private static async Task<UmbracoResponse<List<UserGroupResponse>>> ReadAllAsync(
        IUserGroupClient client,
        CancellationToken ct
    )
    {
        var all = new List<UserGroupResponse>();
        while (true)
        {
            var page = await client.GetUserGroupsAsync(all.Count, PageSize, ct);
            if (!page.IsSuccess)
                return UmbracoResponse<List<UserGroupResponse>>.FailureFrom(page);
            var items = page.Data!.Items.ToList();
            all.AddRange(items);
            // Stop on a short page as well as on the total, so a server that over-reports its
            // total cannot loop forever.
            if (items.Count < PageSize || all.Count >= page.Data.Total)
                return UmbracoResponse<List<UserGroupResponse>>.Success(all);
        }
    }
}
