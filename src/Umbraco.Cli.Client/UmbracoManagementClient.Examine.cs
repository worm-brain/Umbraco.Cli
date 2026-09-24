namespace Umbraco.Cli.Client;

/// <summary>
/// Examine resources on <see cref="UmbracoManagementClient"/> (issue #121): indexes (list/get/rebuild)
/// and searchers (list/query). Index and searcher keys are string names, not GUIDs.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<IndexResponse>>> GetIndexersAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Indexer.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<IndexResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? []).Select(MapIndex).ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<IndexResponse>> GetIndexerAsync(
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var i = await _api
                    .Umbraco.Management.Api.V1.Indexer[name]
                    .GetAsync(cancellationToken: ct);
                return i is null ? new IndexResponse { Name = name } : MapIndex(i);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> RebuildIndexAsync(
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.Indexer[name]
                    .Rebuild.PostAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<SearcherResponse>>> GetSearchersAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.Searcher.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<SearcherResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(s => new SearcherResponse { Name = s.Name ?? "" })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<SearchResultResponse>>> QuerySearcherAsync(
        string name,
        string term,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api
                    .Umbraco.Management.Api.V1.Searcher[name]
                    .Query.GetAsync(
                        c =>
                        {
                            c.QueryParameters.Term = term;
                            c.QueryParameters.Skip = skip;
                            c.QueryParameters.Take = take;
                        },
                        ct
                    );
                return new PagedResponse<SearchResultResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(r => new SearchResultResponse
                        {
                            Id = r.Id ?? "",
                            Score = r.Score ?? 0f,
                            Fields = (r.Fields ?? [])
                                .Select(f => new SearchResultField
                                {
                                    Name = f.Name ?? "",
                                    Values = f.Values ?? [],
                                })
                                .ToList(),
                        })
                        .ToList(),
                };
            }
        );

    /// <summary>Maps a generated index model to the command-facing DTO.</summary>
    /// <param name="i">The generated model.</param>
    /// <returns>The mapped <see cref="IndexResponse"/>.</returns>
    private static IndexResponse MapIndex(Generated.Models.IndexResponseModel i) =>
        new()
        {
            Name = i.Name ?? "",
            // HealthStatus is an object ({status, message}); ToString() on it printed the .NET
            // type name (#243). Read the enum and the message out of it.
            HealthStatus = i.HealthStatus?.Status?.ToString(),
            HealthMessage = i.HealthStatus?.Message,
            DocumentCount = i.DocumentCount ?? 0,
            FieldCount = i.FieldCount ?? 0,
            CanRebuild = i.CanRebuild ?? false,
            SearcherName = i.SearcherName,
        };
}
