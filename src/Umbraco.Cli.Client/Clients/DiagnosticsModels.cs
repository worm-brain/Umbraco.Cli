using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Client;

// Command-facing records for the read-only diagnostics cluster (issue #115): server, health,
// log-viewer, models-builder, and manifest. Kept together here rather than in the shared Models.cs.

// ── Server ─────────────────────────────────────────────────────────────────────

/// <summary>The server's runtime status (e.g. Run, Install, Upgrade).</summary>
public record ServerStatusResponse
{
    /// <summary>The runtime level the server reports.</summary>
    [JsonPropertyName("serverStatus")]
    public string? ServerStatus { get; init; }
}

/// <summary>Version and runtime-mode information about the server.</summary>
public record ServerInformationResponse
{
    /// <summary>The Umbraco version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    /// <summary>The Umbraco assembly version.</summary>
    [JsonPropertyName("assemblyVersion")]
    public string AssemblyVersion { get; init; } = "";

    /// <summary>The server's base UTC offset.</summary>
    [JsonPropertyName("baseUtcOffset")]
    public string BaseUtcOffset { get; init; } = "";

    /// <summary>The runtime mode (e.g. BackofficeDevelopment, Production).</summary>
    [JsonPropertyName("runtimeMode")]
    public string? RuntimeMode { get; init; }
}

/// <summary>Public-facing server configuration flags.</summary>
public record ServerConfigurationResponse
{
    /// <summary>Whether local (non-external) login is allowed.</summary>
    [JsonPropertyName("allowLocalLogin")]
    public bool AllowLocalLogin { get; init; }

    /// <summary>Whether password reset is allowed.</summary>
    [JsonPropertyName("allowPasswordReset")]
    public bool AllowPasswordReset { get; init; }

    /// <summary>The configured backoffice CSS path.</summary>
    [JsonPropertyName("umbracoCssPath")]
    public string UmbracoCssPath { get; init; } = "";

    /// <summary>How often (in days) the server checks for a new version.</summary>
    [JsonPropertyName("versionCheckPeriod")]
    public int VersionCheckPeriod { get; init; }
}

/// <summary>A single server troubleshooting item (name/value pair).</summary>
public record ServerTroubleshootingItem
{
    /// <summary>The item name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The item value.</summary>
    [JsonPropertyName("data")]
    public string Data { get; init; } = "";
}

// ── Health ─────────────────────────────────────────────────────────────────────

/// <summary>A health-check group summary (name only).</summary>
public record HealthCheckGroupSummary
{
    /// <summary>The group name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>A health-check group and the checks it contains.</summary>
public record HealthCheckGroupDetail
{
    /// <summary>The group name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The checks in the group.</summary>
    [JsonPropertyName("checks")]
    public IReadOnlyList<HealthCheckInfo> Checks { get; init; } = [];
}

/// <summary>Metadata about a single health check.</summary>
public record HealthCheckInfo
{
    /// <summary>The check id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The check name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The check description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

/// <summary>The results of running a health-check group.</summary>
public record HealthCheckRunResult
{
    /// <summary>Per-check results.</summary>
    [JsonPropertyName("checks")]
    public IReadOnlyList<HealthCheckRunItem> Checks { get; init; } = [];
}

/// <summary>One check's results within a health-check run.</summary>
public record HealthCheckRunItem
{
    /// <summary>The check id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>
    /// The check's name, from the group read (#370): the run endpoint returns only the id, and the
    /// backoffice shows the name beside each result. Empty when the group does not list the check.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The check's description, from the group read; empty when it has none.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>The individual results the check produced.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<HealthCheckResult> Results { get; init; } = [];
}

/// <summary>A single health-check result.</summary>
public record HealthCheckResult
{
    /// <summary>The result message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    /// <summary>The result status (Success, Warning, Error, Info).</summary>
    [JsonPropertyName("resultType")]
    public string? ResultType { get; init; }

    /// <summary>A "read more" link for the result, if any.</summary>
    [JsonPropertyName("readMoreLink")]
    public string? ReadMoreLink { get; init; }
}

// ── Log viewer ─────────────────────────────────────────────────────────────────

/// <summary>A single log message.</summary>
public record LogMessageResponse
{
    /// <summary>When the entry was logged.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The log level (Verbose, Debug, Information, Warning, Error, Fatal).</summary>
    [JsonPropertyName("level")]
    public string? Level { get; init; }

    /// <summary>The message template (before rendering).</summary>
    [JsonPropertyName("messageTemplate")]
    public string MessageTemplate { get; init; } = "";

    /// <summary>The rendered message.</summary>
    [JsonPropertyName("renderedMessage")]
    public string RenderedMessage { get; init; } = "";

    /// <summary>The exception text, if any.</summary>
    [JsonPropertyName("exception")]
    public string? Exception { get; init; }
}

/// <summary>A logger and its configured minimum level.</summary>
public record LoggerLevelResponse
{
    /// <summary>The logger name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The configured level.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; init; }
}

/// <summary>Counts of log messages by level over a period.</summary>
public record LogLevelCounts
{
    /// <summary>Debug-level count.</summary>
    [JsonPropertyName("debug")]
    public int Debug { get; init; }

    /// <summary>Information-level count.</summary>
    [JsonPropertyName("information")]
    public int Information { get; init; }

    /// <summary>Warning-level count.</summary>
    [JsonPropertyName("warning")]
    public int Warning { get; init; }

    /// <summary>Error-level count.</summary>
    [JsonPropertyName("error")]
    public int Error { get; init; }

    /// <summary>Fatal-level count.</summary>
    [JsonPropertyName("fatal")]
    public int Fatal { get; init; }
}

/// <summary>A message template and how often it occurred.</summary>
public record LogTemplateResponse
{
    /// <summary>The message template.</summary>
    [JsonPropertyName("messageTemplate")]
    public string MessageTemplate { get; init; } = "";

    /// <summary>How many messages used this template.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>A saved log-viewer search.</summary>
public record SavedLogSearchResponse
{
    /// <summary>The saved search name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The saved query.</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = "";
}

// ── Models builder ───────────────────────────────────────────────────────────────

/// <summary>The models-builder dashboard status.</summary>
public record ModelsBuilderDashboard
{
    /// <summary>The models-builder mode (e.g. InMemoryAuto, SourceCodeManual).</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>Whether models can be generated in the current mode.</summary>
    [JsonPropertyName("canGenerate")]
    public bool CanGenerate { get; init; }

    /// <summary>Whether the generated models are out of date.</summary>
    [JsonPropertyName("outOfDateModels")]
    public bool OutOfDateModels { get; init; }

    /// <summary>Whether out-of-date tracking is enabled.</summary>
    [JsonPropertyName("trackingOutOfDateModels")]
    public bool TrackingOutOfDateModels { get; init; }

    /// <summary>The last generation error, if any.</summary>
    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }

    /// <summary>The models-builder version.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>The namespace generated models are placed in.</summary>
    [JsonPropertyName("modelsNamespace")]
    public string? ModelsNamespace { get; init; }
}

/// <summary>The models-builder out-of-date status.</summary>
public record ModelsBuilderStatus
{
    /// <summary>The out-of-date status (e.g. Current, OutOfDate, Unknown).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

// ── Manifest ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A package manifest. The opaque <c>extensions</c> payload is not projected (it is arbitrary JSON);
/// this surfaces the identifying metadata, plus what the package declares about the CLI.
/// </summary>
public record ManifestResponse
{
    /// <summary>The manifest id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>The manifest name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The manifest version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    /// <summary>
    /// The merged <c>meta</c> of the manifest's <c>umbracoCli</c> extensions (ADR 0009, #440), or
    /// null when the package declares nothing about the CLI.
    /// </summary>
    [JsonPropertyName("cliCapabilities")]
    public JsonObject? CliCapabilities { get; init; }
}
