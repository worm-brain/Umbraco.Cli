namespace Umbraco.Cli.Client;

// Read-only (plus a few explicit action) diagnostics resources (issue #115): server, health,
// log-viewer, models-builder, and manifest. One role interface per noun so consumers and the fake
// depend only on what they use.

/// <summary>Read-only server status and information (issue #115).</summary>
public interface IServerClient
{
    /// <summary>Gets the server's runtime status.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server status.</returns>
    Task<UmbracoResponse<ServerStatusResponse>> GetServerStatusAsync(
        CancellationToken ct = default
    );

    /// <summary>Gets server version and runtime-mode information.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server information.</returns>
    Task<UmbracoResponse<ServerInformationResponse>> GetServerInformationAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Best-effort product version of the connected server, resolved lazily and cached for the
    /// life of the client (#152). Used to annotate error output so a failure can be attributed to
    /// a server version; never throws, and returns null when the version cannot be determined.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server product version, or null when unknown.</returns>
    Task<string?> GetServerVersionAsync(CancellationToken ct = default);

    /// <summary>Gets public server configuration flags.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The server configuration.</returns>
    Task<UmbracoResponse<ServerConfigurationResponse>> GetServerConfigurationAsync(
        CancellationToken ct = default
    );

    /// <summary>Gets the server troubleshooting items.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The troubleshooting name/value items.</returns>
    Task<UmbracoResponse<IReadOnlyList<ServerTroubleshootingItem>>> GetServerTroubleshootingAsync(
        CancellationToken ct = default
    );
}

/// <summary>Health-check groups: list, inspect, and run (issue #115).</summary>
public interface IHealthClient
{
    /// <summary>Lists the health-check groups.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of group summaries.</returns>
    Task<UmbracoResponse<PagedResponse<HealthCheckGroupSummary>>> GetHealthCheckGroupsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets a health-check group and the checks it contains.</summary>
    /// <param name="name">The group name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The group detail.</returns>
    Task<UmbracoResponse<HealthCheckGroupDetail>> GetHealthCheckGroupAsync(
        string name,
        CancellationToken ct = default
    );

    /// <summary>
    /// Runs a health-check group and returns its results, each check carrying its name and
    /// description from the group read (#370).
    /// </summary>
    /// <param name="name">The group name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run results.</returns>
    Task<UmbracoResponse<HealthCheckRunResult>> RunHealthCheckGroupAsync(
        string name,
        CancellationToken ct = default
    );
}

/// <summary>Log-viewer reads plus saved-search management (issue #115).</summary>
public interface ILogViewerClient
{
    /// <summary>Lists log messages, optionally filtered.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="levels">Log levels to include; null/empty for all.</param>
    /// <param name="filterExpression">A free-text/Serilog filter expression; null for none.</param>
    /// <param name="startDate">Start of the date range; null for no lower bound.</param>
    /// <param name="endDate">End of the date range; null for no upper bound.</param>
    /// <param name="descending">Whether to order newest-first (descending).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of log messages.</returns>
    Task<UmbracoResponse<PagedResponse<LogMessageResponse>>> GetLogsAsync(
        int skip = 0,
        int take = 100,
        IReadOnlyList<LogLevel>? levels = null,
        string? filterExpression = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        bool descending = true,
        CancellationToken ct = default
    );

    /// <summary>Lists the configured loggers and their levels.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of loggers.</returns>
    Task<UmbracoResponse<PagedResponse<LoggerLevelResponse>>> GetLogLevelsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Gets message counts by level over a date range.</summary>
    /// <param name="startDate">Start of the date range; null for no lower bound.</param>
    /// <param name="endDate">End of the date range; null for no upper bound.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The per-level counts.</returns>
    Task<UmbracoResponse<LogLevelCounts>> GetLogLevelCountsAsync(
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default
    );

    /// <summary>Lists the most common message templates over a date range.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="startDate">Start of the date range; null for no lower bound.</param>
    /// <param name="endDate">End of the date range; null for no upper bound.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of message templates.</returns>
    Task<UmbracoResponse<PagedResponse<LogTemplateResponse>>> GetLogMessageTemplatesAsync(
        int skip = 0,
        int take = 100,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default
    );

    /// <summary>Lists the saved log searches.</summary>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of saved searches.</returns>
    Task<UmbracoResponse<PagedResponse<SavedLogSearchResponse>>> GetSavedLogSearchesAsync(
        int skip = 0,
        int take = 100,
        CancellationToken ct = default
    );

    /// <summary>Creates a saved log search.</summary>
    /// <param name="name">The search name.</param>
    /// <param name="query">The query to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created saved search (echoed), or a mapped failure.</returns>
    Task<UmbracoResponse<SavedLogSearchResponse>> CreateSavedLogSearchAsync(
        string name,
        string query,
        CancellationToken ct = default
    );

    /// <summary>Deletes a saved log search by name.</summary>
    /// <param name="name">The search name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteSavedLogSearchAsync(
        string name,
        CancellationToken ct = default
    );
}

/// <summary>Models-builder status plus the build action (issue #115).</summary>
public interface IModelsBuilderClient
{
    /// <summary>Gets the models-builder dashboard status.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dashboard status.</returns>
    Task<UmbracoResponse<ModelsBuilderDashboard>> GetModelsBuilderDashboardAsync(
        CancellationToken ct = default
    );

    /// <summary>Gets the models-builder out-of-date status.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The out-of-date status.</returns>
    Task<UmbracoResponse<ModelsBuilderStatus>> GetModelsBuilderStatusAsync(
        CancellationToken ct = default
    );

    /// <summary>Triggers a models-builder build (regenerates model source files on the server).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> BuildModelsAsync(CancellationToken ct = default);
}

/// <summary>Read-only listing of package manifests (issue #115).</summary>
public interface IManifestClient
{
    /// <summary>Lists package manifests.</summary>
    /// <param name="scope">Which manifests to list: all, public-only, or private-only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The manifests (a bare array; the API does not page this resource).</returns>
    Task<UmbracoResponse<IReadOnlyList<ManifestResponse>>> GetManifestsAsync(
        ManifestScope scope = ManifestScope.All,
        CancellationToken ct = default
    );
}

/// <summary>
/// Log levels for the <c>log-viewer list</c> filter (issue #115). Mirrors the Management API's log
/// levels; a typed option means an unknown level is rejected at parse time rather than silently
/// dropped.
/// </summary>
public enum LogLevel
{
    /// <summary>Verbose (most detailed).</summary>
    Verbose,

    /// <summary>Debug.</summary>
    Debug,

    /// <summary>Information.</summary>
    Information,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,

    /// <summary>Fatal (most severe).</summary>
    Fatal,
}

/// <summary>Which manifests to list (issue #115).</summary>
public enum ManifestScope
{
    /// <summary>All manifests.</summary>
    All,

    /// <summary>Public manifests only.</summary>
    Public,

    /// <summary>Private manifests only.</summary>
    Private,
}
