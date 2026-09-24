using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Wiring for the schema commands that accept a full Management API body (#161/#169).
/// <para>
/// A document type's properties and groups, and a data type's editor configuration, are too
/// structured to express as flags - which is why authoring either one meant a
/// <c>schema export -&gt; jq -&gt; schema apply</c> round-trip through the whole instance. These
/// commands take the body directly and hand it to the raw client, so the shape a caller edits is
/// the shape the API defines, with nothing in between to drift.
/// </para>
/// <para>
/// <c>--schema</c> reads a real type off the instance and prints it as a worked example. That
/// makes it host-requiring, unlike the typed <c>--schema</c> on <c>content create</c> - a
/// deliberate trade: a hand-written schema beside a passthrough body is a second definition that
/// goes stale silently, which is the failure this whole plan keeps finding.
/// </para>
/// </summary>
public static class RawBodyCommand
{
    /// <summary>Adds the <c>--json-body</c> / <c>--schema</c> pair used by the raw schema verbs.</summary>
    /// <param name="cmd">The command to add them to.</param>
    /// <returns>The body option handle.</returns>
    public static JsonBodyOption AddBodyOptions(Command cmd)
    {
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) containing the full Management API body. "
                + "Use --schema to print a real one from the instance as a starting point."
        );
        body.AddTo(cmd);
        return body;
    }

    /// <summary>
    /// Reads an existing entity of this kind and returns it as a worked example for
    /// <c>--schema</c>.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="listIds">Lists the ids of this kind.</param>
    /// <param name="getRaw">Reads one verbatim by id.</param>
    /// <param name="kind">The noun, for the error when the instance has none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An example body, or a failure explaining why there is none.</returns>
    public static async Task<UmbracoResponse<JsonNode>> ExampleAsync(
        IUmbracoManagementClient client,
        Func<CancellationToken, Task<UmbracoResponse<IReadOnlyList<Guid>>>> listIds,
        Func<Guid, CancellationToken, Task<UmbracoResponse<JsonNode>>> getRaw,
        string kind,
        CancellationToken ct
    )
    {
        var ids = await listIds(ct);
        if (!ids.IsSuccess)
            return UmbracoResponse<JsonNode>.Failure(
                ids.StatusCode,
                ids.ErrorMessage!,
                ids.Category
            );

        if (ids.Data is not { Count: > 0 } found)
            return UmbracoResponse<JsonNode>.Failure(
                404,
                $"This instance has no {kind} to use as an example. Create one first, or write "
                    + "the body against the Umbraco Management API reference."
            );

        return await getRaw(found[0], ct);
    }
}
