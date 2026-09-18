using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Read-only diagnostics resources on <see cref="UmbracoManagementClient"/> (issue #115): server,
/// health, log-viewer, models-builder, and manifest - plus the few explicit action endpoints
/// (health run, models-builder build, saved-search create/delete). Generated enum values are
/// surfaced as their string names; all listing endpoints map to <see cref="PagedResponse{T}"/>
/// except manifest, which the API returns as a bare array.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    // ── Server ─────────────────────────────────────────────────────────────────

    /// <summary>Cached product version of the connected server; null once resolved-and-unknown.</summary>
    private string? _cachedServerVersion;

    /// <summary>Whether <see cref="GetServerVersionAsync"/> has already run (so it is not retried per error).</summary>
    private bool _serverVersionResolved;

    /// <inheritdoc />
    public async Task<string?> GetServerVersionAsync(CancellationToken ct = default)
    {
        // Lazy + cached per client (#152): a version lookup is only ever wanted to annotate an
        // error, so it is fetched on demand, at most once, and never retried - a repeated failure
        // (e.g. mid-bulk) must not add a round-trip each time. GetServerInformationAsync is guarded
        // and never throws, so a failed lookup returns null and can never mask the real error.
        if (_serverVersionResolved)
            return _cachedServerVersion;

        var info = await GetServerInformationAsync(ct);
        _cachedServerVersion =
            info.IsSuccess && !string.IsNullOrWhiteSpace(info.Data?.Version)
                ? info.Data!.Version
                : null;
        _serverVersionResolved = true;
        return _cachedServerVersion;
    }

    /// <inheritdoc />
    public Task<UmbracoResponse<ServerStatusResponse>> GetServerStatusAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var s = await _api.Umbraco.Management.Api.V1.Server.Status.GetAsync(
                    cancellationToken: ct
                );
                return new ServerStatusResponse { ServerStatus = s?.ServerStatus?.ToString() };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<ServerInformationResponse>> GetServerInformationAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var i = await _api.Umbraco.Management.Api.V1.Server.Information.GetAsync(
                    cancellationToken: ct
                );
                return new ServerInformationResponse
                {
                    Version = i?.Version ?? "",
                    AssemblyVersion = i?.AssemblyVersion ?? "",
                    BaseUtcOffset = i?.BaseUtcOffset ?? "",
                    RuntimeMode = i?.RuntimeMode?.ToString(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<ServerConfigurationResponse>> GetServerConfigurationAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var c = await _api.Umbraco.Management.Api.V1.Server.Configuration.GetAsync(
                    cancellationToken: ct
                );
                return new ServerConfigurationResponse
                {
                    AllowLocalLogin = c?.AllowLocalLogin ?? false,
                    AllowPasswordReset = c?.AllowPasswordReset ?? false,
                    UmbracoCssPath = c?.UmbracoCssPath ?? "",
                    VersionCheckPeriod = c?.VersionCheckPeriod ?? 0,
                };
            }
        );

    /// <inheritdoc />
    public Task<
        UmbracoResponse<IReadOnlyList<ServerTroubleshootingItem>>
    > GetServerTroubleshootingAsync(CancellationToken ct = default) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var t = await _api.Umbraco.Management.Api.V1.Server.Troubleshooting.GetAsync(
                    cancellationToken: ct
                );
                return (IReadOnlyList<ServerTroubleshootingItem>)
                    (t?.Items ?? [])
                        .Select(i => new ServerTroubleshootingItem
                        {
                            Name = i.Name ?? "",
                            Data = i.Data ?? "",
                        })
                        .ToList();
            }
        );

    // ── Health ─────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<HealthCheckGroupSummary>>> GetHealthCheckGroupsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.HealthCheckGroup.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<HealthCheckGroupSummary>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(g => new HealthCheckGroupSummary { Name = g.Name ?? "" })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<HealthCheckGroupDetail>> GetHealthCheckGroupAsync(
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var g = await _api
                    .Umbraco.Management.Api.V1.HealthCheckGroup[name]
                    .GetAsync(cancellationToken: ct);
                return new HealthCheckGroupDetail
                {
                    Name = g?.Name ?? name,
                    Checks = (g?.Checks ?? [])
                        .Select(c => new HealthCheckInfo
                        {
                            Id = c.Id ?? Guid.Empty,
                            Name = c.Name ?? "",
                            Description = c.Description ?? "",
                        })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<HealthCheckRunResult>> RunHealthCheckGroupAsync(
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var r = await _api
                    .Umbraco.Management.Api.V1.HealthCheckGroup[name]
                    .Check.PostAsync(cancellationToken: ct);
                return new HealthCheckRunResult
                {
                    Checks = (r?.Checks ?? [])
                        .Select(c => new HealthCheckRunItem
                        {
                            Id = c.Id ?? Guid.Empty,
                            Results = (c.Results ?? [])
                                .Select(res => new HealthCheckResult
                                {
                                    Message = res.Message ?? "",
                                    ResultType = res.ResultType?.ToString(),
                                    ReadMoreLink = res.ReadMoreLink,
                                })
                                .ToList(),
                        })
                        .ToList(),
                };
            }
        );

    // ── Log viewer ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<LogMessageResponse>>> GetLogsAsync(
        int skip = 0,
        int take = 100,
        IReadOnlyList<LogLevel>? levels = null,
        string? filterExpression = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        bool descending = true,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var parsedLevels = (levels ?? []).Select(ToGenLogLevel).ToArray();
                var paged = await _api.Umbraco.Management.Api.V1.LogViewer.Log.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                        c.QueryParameters.OrderDirection = descending
                            ? Gen.DirectionModel.Descending
                            : Gen.DirectionModel.Ascending;
                        if (!string.IsNullOrEmpty(filterExpression))
                            c.QueryParameters.FilterExpression = filterExpression;
                        if (parsedLevels.Length > 0)
                            c.QueryParameters.LogLevel = parsedLevels;
                        if (startDate is { } sd)
                            c.QueryParameters.StartDate = sd;
                        if (endDate is { } ed)
                            c.QueryParameters.EndDate = ed;
                    },
                    ct
                );
                return new PagedResponse<LogMessageResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(m => new LogMessageResponse
                        {
                            Timestamp = m.Timestamp ?? default,
                            Level = m.Level?.ToString(),
                            MessageTemplate = m.MessageTemplate ?? "",
                            RenderedMessage = m.RenderedMessage ?? "",
                            Exception = m.Exception,
                        })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<LoggerLevelResponse>>> GetLogLevelsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.LogViewer.Level.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<LoggerLevelResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(l => new LoggerLevelResponse
                        {
                            Name = l.Name ?? "",
                            Level = l.Level?.ToString(),
                        })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<LogLevelCounts>> GetLogLevelCountsAsync(
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var c = await _api.Umbraco.Management.Api.V1.LogViewer.LevelCount.GetAsync(
                    r =>
                    {
                        if (startDate is { } sd)
                            r.QueryParameters.StartDate = sd;
                        if (endDate is { } ed)
                            r.QueryParameters.EndDate = ed;
                    },
                    ct
                );
                return new LogLevelCounts
                {
                    Debug = c?.Debug ?? 0,
                    Information = c?.Information ?? 0,
                    Warning = c?.Warning ?? 0,
                    Error = c?.Error ?? 0,
                    Fatal = c?.Fatal ?? 0,
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<LogTemplateResponse>>> GetLogMessageTemplatesAsync(
        int skip = 0,
        int take = 100,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.LogViewer.MessageTemplate.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                        if (startDate is { } sd)
                            c.QueryParameters.StartDate = sd;
                        if (endDate is { } ed)
                            c.QueryParameters.EndDate = ed;
                    },
                    ct
                );
                return new PagedResponse<LogTemplateResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(t => new LogTemplateResponse
                        {
                            MessageTemplate = t.MessageTemplate ?? "",
                            Count = (int)(t.Count ?? 0),
                        })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<SavedLogSearchResponse>>> GetSavedLogSearchesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var paged = await _api.Umbraco.Management.Api.V1.LogViewer.SavedSearch.GetAsync(
                    c =>
                    {
                        c.QueryParameters.Skip = skip;
                        c.QueryParameters.Take = take;
                    },
                    ct
                );
                return new PagedResponse<SavedLogSearchResponse>
                {
                    Total = (int)(paged?.Total ?? 0),
                    Items = (paged?.Items ?? [])
                        .Select(s => new SavedLogSearchResponse
                        {
                            Name = s.Name ?? "",
                            Query = s.Query ?? "",
                        })
                        .ToList(),
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<SavedLogSearchResponse>> CreateSavedLogSearchAsync(
        string name,
        string query,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.LogViewer.SavedSearch.PostAsync(
                    new Gen.SavedLogSearchRequestModel { Name = name, Query = query },
                    cancellationToken: ct
                );
                // The create returns no body; echo the saved search back to the caller.
                return new SavedLogSearchResponse { Name = name, Query = query };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteSavedLogSearchAsync(
        string name,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api
                    .Umbraco.Management.Api.V1.LogViewer.SavedSearch[name]
                    .DeleteAsync(cancellationToken: ct);
                return Empty.Value;
            }
        );

    // ── Models builder ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<ModelsBuilderDashboard>> GetModelsBuilderDashboardAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var d = await _api.Umbraco.Management.Api.V1.ModelsBuilder.Dashboard.GetAsync(
                    cancellationToken: ct
                );
                return new ModelsBuilderDashboard
                {
                    Mode = d?.Mode,
                    CanGenerate = d?.CanGenerate ?? false,
                    OutOfDateModels = d?.OutOfDateModels ?? false,
                    TrackingOutOfDateModels = d?.TrackingOutOfDateModels ?? false,
                    LastError = d?.LastError,
                    Version = d?.Version,
                    ModelsNamespace = d?.ModelsNamespace,
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<ModelsBuilderStatus>> GetModelsBuilderStatusAsync(
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var s = await _api.Umbraco.Management.Api.V1.ModelsBuilder.Status.GetAsync(
                    cancellationToken: ct
                );
                return new ModelsBuilderStatus { Status = s?.Status?.ToString() };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> BuildModelsAsync(CancellationToken ct = default) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                await _api.Umbraco.Management.Api.V1.ModelsBuilder.Build.PostAsync(
                    cancellationToken: ct
                );
                return Empty.Value;
            }
        );

    // ── Manifest ─────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<UmbracoResponse<IReadOnlyList<ManifestResponse>>> GetManifestsAsync(
        ManifestScope scope = ManifestScope.All,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var manifest = _api.Umbraco.Management.Api.V1.Manifest.Manifest;
                var list = scope switch
                {
                    ManifestScope.Public => await manifest.Public.GetAsync(cancellationToken: ct),
                    ManifestScope.Private => await manifest.Private.GetAsync(cancellationToken: ct),
                    _ => await manifest.GetAsync(cancellationToken: ct),
                };
                return (IReadOnlyList<ManifestResponse>)
                    (list ?? [])
                        .Select(m => new ManifestResponse
                        {
                            Id = m.Id ?? "",
                            Name = m.Name ?? "",
                            Version = m.Version ?? "",
                        })
                        .ToList();
            }
        );

    /// <summary>Maps a command-facing <see cref="LogLevel"/> to the generated log-level enum.</summary>
    /// <param name="level">The command-facing level (already validated at parse time).</param>
    /// <returns>The equivalent <see cref="Gen.LogLevelModel"/>.</returns>
    private static Gen.LogLevelModel ToGenLogLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Verbose => Gen.LogLevelModel.Verbose,
            LogLevel.Debug => Gen.LogLevelModel.Debug,
            LogLevel.Information => Gen.LogLevelModel.Information,
            LogLevel.Warning => Gen.LogLevelModel.Warning,
            LogLevel.Error => Gen.LogLevelModel.Error,
            LogLevel.Fatal => Gen.LogLevelModel.Fatal,
            _ => Gen.LogLevelModel.Information,
        };
}
