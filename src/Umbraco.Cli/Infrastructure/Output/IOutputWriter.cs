using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Infrastructure.Output;

public enum OutputFormat
{
    Human,
    Json,
    Csv,
}

/// <summary>
/// What a list result can say about the rest of the data (#173). A list that truncates silently
/// is indistinguishable from a complete one, which is how 17 of 37 data types went missing during
/// the 2026-09-23 test round without anything looking wrong.
/// </summary>
/// <param name="Total">Total items matching the query, or null when the source cannot say.</param>
/// <param name="Skip">The offset this page started at, or null when not paged.</param>
/// <param name="Take">The page size requested, or null when not paged.</param>
public readonly record struct ListPaging(int? Total, int? Skip, int? Take)
{
    /// <summary>A list with no paging information - everything returned is everything there is.</summary>
    public static ListPaging Unknown => new(null, null, null);

    /// <summary>
    /// A list that is complete by construction - a computed report such as a diff - so it can say
    /// so: <c>total</c> is its count and <c>hasMore</c> is false.
    /// </summary>
    /// <param name="count">How many items the list has.</param>
    /// <returns>The paging.</returns>
    public static ListPaging Complete(int count) => new(count, 0, null);

    /// <summary>
    /// Whether more items exist beyond this page, given how many it actually returned. Null when
    /// <see cref="Total"/> is unknown, since "probably not" is exactly the guess that hides a
    /// truncated list. Counts delivered items rather than the requested page size, so a short or
    /// filtered page reports the truth.
    /// </summary>
    /// <param name="delivered">How many items this page actually returned.</param>
    /// <returns>Whether more items exist beyond this page.</returns>
    public bool? HasMoreAfter(int delivered) =>
        Total is { } total && Skip is { } skip ? skip + delivered < total : null;
}

public interface IOutputWriter
{
    /// <summary>
    /// Adds a command-specific field to the <c>meta</c> of every success envelope this writer emits
    /// from now on, such as <c>dictionary get</c>'s <c>valueFormat</c> (#440). Declared per command
    /// through <see cref="CommandMeta"/>. Writers without a <c>meta</c> (human, CSV) ignore it,
    /// which is what this default does.
    /// </summary>
    /// <param name="key">The field name, camelCase.</param>
    /// <param name="value">The value; null leaves the field out, like every other absent meta field.</param>
    void AddMeta(string key, object? value) { }

    void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null);

    /// <summary>
    /// Writes an error to stderr. <paramref name="category"/> and <paramref name="serverVersion"/>
    /// classify the failure (#152) so a caller can tell whose problem it is and against which
    /// server. The category is required; the server version only applies to failures the
    /// server responded to.
    /// <para>
    /// <paramref name="exitCode"/> and <paramref name="httpStatus"/> are deliberately separate
    /// (#177): one field used to carry whichever of the two applied, so a caller could not act on
    /// it without already knowing which kind of failure it had.
    /// </para>
    /// </summary>
    /// <param name="exitCode">The process exit code this failure produces.</param>
    /// <param name="category">Why it failed. Required: every error carries a category (docs/conventions.md, section 7).</param>
    /// <param name="message">The human-readable error message.</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>; null only when no command was parsed.</param>
    /// <param name="httpStatus">The server's HTTP status, or null when the failure never reached it.</param>
    /// <param name="serverVersion">The connected server's version, or null when unknown/not applicable.</param>
    /// <param name="details">
    /// Umbraco's error body (its ProblemDetails) as it sent it, or null when there was none
    /// (#286). Structured writers emit it as <c>details</c>; the human and CSV writers rely on
    /// <paramref name="message"/>, which already names what to fix.
    /// </param>
    void WriteError(
        ExitCode exitCode,
        FailureCategory category,
        string message,
        string? commandName,
        int? httpStatus = null,
        string? serverVersion = null,
        JsonNode? details = null
    );

    /// <summary>
    /// Renders a list result as a table. <paramref name="commandName"/> and
    /// <paramref name="durationMs"/> populate the same <c>meta</c> fields that
    /// <see cref="WriteSuccess"/> emits, so list output carries the same envelope contract as
    /// object output (#137) - a structured writer with no <c>meta</c> (CSV/human) ignores them.
    /// </summary>
    /// <param name="headers">Column headers (camelCased into field keys by structured writers).</param>
    /// <param name="rows">The row cells, aligned to <paramref name="headers"/>.</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>, or null.</param>
    /// <param name="durationMs">The command duration for <c>meta.durationMs</c>, or null.</param>
    void WriteTable(
        string[] headers,
        IEnumerable<string[]> rows,
        string? commandName = null,
        long? durationMs = null
    );

    /// <summary>
    /// Renders a list result (#164/#173).
    /// <para>
    /// The two audiences are served from different inputs on purpose. Structured writers
    /// serialize <paramref name="items"/> - the DTOs themselves - so a list's field names and
    /// types match the matching <c>get</c> automatically and permanently. The human table is
    /// rendered from <paramref name="headers"/> and <paramref name="rows"/>, which exist only to
    /// be read by a person and are free to abbreviate.
    /// </para>
    /// <para>
    /// Deriving the JSON keys from the human captions is what produced <c>"published": "True"</c>
    /// against <c>get</c>'s <c>"isPublished": true</c>, and made every value a string - see
    /// <see cref="WriteTable"/>, which this replaces for list commands.
    /// </para>
    /// </summary>
    /// <param name="items">The items to serialize for structured output.</param>
    /// <param name="headers">Column headers for the human table.</param>
    /// <param name="rows">Row cells for the human table, aligned to <paramref name="headers"/>.</param>
    /// <param name="paging">Paging facts for <c>meta</c>; <see cref="ListPaging.Unknown"/> when there are none.</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>, or null.</param>
    /// <param name="durationMs">The command duration for <c>meta.durationMs</c>, or null.</param>
    void WriteList(
        IReadOnlyList<object> items,
        string[] headers,
        IEnumerable<string[]> rows,
        ListPaging paging,
        string? commandName = null,
        long? durationMs = null
    );

    /// <summary>
    /// Renders the result of a write (delete, move, update...). Structured writers emit
    /// <paramref name="data"/> like any success - every success has <c>data</c>
    /// (docs/conventions.md 6.2) - and the human writer shows <paramref name="message"/>.
    /// </summary>
    /// <param name="data">The resulting item, or an <see cref="Commands.ItemRef"/> naming what was acted on.</param>
    /// <param name="message">The human confirmation, e.g. "Deleted.".</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>, or null.</param>
    /// <param name="durationMs">The command duration for <c>meta.durationMs</c>, or null.</param>
    void WriteMessage(
        object data,
        string message,
        string? commandName = null,
        long? durationMs = null
    );

    /// <summary>
    /// Renders a <c>--dry-run</c> preview of the write request that would have been sent,
    /// without executing it. Written to stdout (it is a successful preview, not an error) so
    /// agents can read the intended request from the normal output stream.
    /// </summary>
    /// <param name="method">The HTTP method that would be sent (e.g. <c>POST</c>).</param>
    /// <param name="url">The absolute request URL.</param>
    /// <param name="body">The request body, or null for a body-less request (e.g. DELETE).</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>.</param>
    /// <param name="durationMs">The command duration for <c>meta.durationMs</c>, or null.</param>
    /// <param name="then">
    /// The requests a multi-step write would send after the first, in order (#353); null or empty
    /// for a single-request write.
    /// </param>
    void WriteDryRun(
        string method,
        string url,
        string? body,
        string? commandName,
        long? durationMs = null,
        IReadOnlyList<Umbraco.Cli.Infrastructure.Http.PreviewedRequest>? then = null
    );

    /// <summary>
    /// Writes a bulk run's per-item results (#236). The structured writer sets the envelope
    /// status from the outcomes (<c>success</c> / <c>partial</c> / <c>error</c> / <c>dry-run</c>)
    /// and adds <c>meta.summary</c>; by default the results are written like any other payload.
    /// Written to stdout whatever the outcome: it is a report on every item, not one error.
    /// <para>
    /// No default body: a wrapper (e.g. <c>--quiet</c>) that fell back to
    /// <see cref="WriteSuccess{T}"/> silently reported every bulk run as <c>success</c> again.
    /// </para>
    /// </summary>
    /// <param name="results">The per-item results, in input order.</param>
    /// <param name="commandName">The dotted command name for <c>meta.command</c>, or null.</param>
    /// <param name="durationMs">How long the run took, or null.</param>
    void WriteBulk(
        IReadOnlyList<BulkItemResult> results,
        string? commandName = null,
        long? durationMs = null
    );
}
