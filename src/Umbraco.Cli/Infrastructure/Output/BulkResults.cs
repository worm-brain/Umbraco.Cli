using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>One item's outcome in a bulk run (#85). Serialized as <c>success</c> / <c>error</c> / <c>dry-run</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BulkItemStatus>))]
public enum BulkItemStatus
{
    /// <summary>The item's call succeeded.</summary>
    [JsonStringEnumMemberName("success")]
    Success,

    /// <summary>The item failed (an API error, a malformed id, a blocked write).</summary>
    [JsonStringEnumMemberName("error")]
    Error,

    /// <summary>The item was previewed under <c>--dry-run</c>; nothing was sent.</summary>
    [JsonStringEnumMemberName("dry-run")]
    DryRun,
}

/// <summary>The wire names of <see cref="BulkItemStatus"/>, for writers that do not go through JSON.</summary>
public static class BulkItemStatusExtensions
{
    /// <summary>The status as it appears on the wire: <c>success</c>, <c>error</c> or <c>dry-run</c>.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The wire name.</returns>
    public static string ToWire(this BulkItemStatus status) =>
        status switch
        {
            BulkItemStatus.Success => "success",
            BulkItemStatus.DryRun => "dry-run",
            _ => "error",
        };
}

/// <summary>
/// One item's outcome in a bulk operation (#85): the id as supplied, its status, and an error
/// message when it failed. Serialized into the results array so a script can see exactly what
/// happened to each id.
/// </summary>
/// <param name="Id">The id as it appeared in the input (so a malformed line is echoed back).</param>
/// <param name="Status">The per-item outcome.</param>
/// <param name="Error">The failure message when <see cref="Status"/> is <see cref="BulkItemStatus.Error"/>; otherwise null.</param>
/// <param name="Request">Under <c>--dry-run</c>, the request that would have been sent for this id (#236); otherwise null.</param>
public sealed record BulkItemResult(
    string Id,
    BulkItemStatus Status,
    string? Error,
    BulkRequest? Request = null
);

/// <summary>A request a bulk <c>--dry-run</c> would have sent for one item (#236).</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Url">The absolute request URL.</param>
/// <param name="Body">The body as <see cref="JsonBody.Parse"/> reads it, or null.</param>
public sealed record BulkRequest(string Method, string Url, object? Body)
{
    /// <summary>Builds the request from a captured dry run, nesting a JSON body as JSON.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The absolute request URL.</param>
    /// <param name="body">The raw body, or null.</param>
    /// <returns>The request.</returns>
    public static BulkRequest From(string method, string url, string? body) =>
        new(method, url, JsonBody.Parse(body));
}

/// <summary>
/// How a bulk run went overall (#236): the counts, the envelope status they imply, and the exit code.
/// </summary>
/// <param name="Succeeded">Items that succeeded.</param>
/// <param name="Failed">Items that failed.</param>
/// <param name="DryRun">Items previewed under <c>--dry-run</c>.</param>
public sealed record BulkSummary(int Succeeded, int Failed, int DryRun)
{
    /// <summary>Counts the outcomes in <paramref name="results"/>.</summary>
    /// <param name="results">The per-item results.</param>
    /// <returns>The summary.</returns>
    public static BulkSummary Of(IReadOnlyList<BulkItemResult> results) =>
        new(
            results.Count(r => r.Status == BulkItemStatus.Success),
            results.Count(r => r.Status == BulkItemStatus.Error),
            results.Count(r => r.Status == BulkItemStatus.DryRun)
        );

    /// <summary>
    /// The envelope status: <c>error</c> when every item failed, <c>partial</c> when some did,
    /// <c>dry-run</c> when items were previewed and none failed, <c>success</c> otherwise. Before
    /// #236 it was always <c>success</c>, even when every item failed and the exit code was 1.
    /// </summary>
    public string Status =>
        Failed > 0 && Succeeded == 0 && DryRun == 0 ? "error"
        : Failed > 0 ? "partial"
        : DryRun > 0 ? "dry-run"
        : "success";

    /// <summary>The process exit code: <c>1</c> when any item failed, <c>0</c> otherwise.</summary>
    public int ExitCode =>
        (int)(Failed > 0 ? Infrastructure.ExitCode.Failed : Infrastructure.ExitCode.Success);
}

/// <summary>Reads a captured request body for display in a dry-run preview.</summary>
public static class JsonBody
{
    /// <summary>
    /// The body as parsed JSON when it is valid JSON, so a preview nests cleanly for agents;
    /// otherwise the raw text; null when there is no body. Shared by the single and bulk dry-run
    /// output so both render a body the same way.
    /// </summary>
    /// <param name="body">The raw body, or null.</param>
    /// <returns>A <see cref="JsonElement"/>, the raw string, or null.</returns>
    public static object? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
