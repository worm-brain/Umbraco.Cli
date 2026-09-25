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
                + "Use --schema to print a real one from the instance as a starting point.",
            "Print an existing entity of this kind from the instance, as a worked example of the "
                + "--json-body shape, and exit. Requires a host."
        );
        body.AddTo(cmd);
        return body;
    }

    /// <summary>
    /// Runs the <c>--schema</c> branch: prints a real entity of this kind off the instance.
    /// Identical at all four schema verbs, so it lives here rather than four times over.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The command name for the output envelope.</param>
    /// <param name="listIds">Lists the ids of this kind, given a client.</param>
    /// <param name="getRaw">Reads one verbatim by id, given a client.</param>
    /// <param name="kind">The noun, for the error when the instance has none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    public static Task<int> RunSchemaAsync(
        CommandExecutor executor,
        ParseResult parseResult,
        string commandName,
        Func<
            IUmbracoManagementClient,
            Func<CancellationToken, Task<UmbracoResponse<IReadOnlyList<Guid>>>>
        > listIds,
        Func<
            IUmbracoManagementClient,
            Func<Guid, CancellationToken, Task<UmbracoResponse<JsonNode>>>
        > getRaw,
        string kind,
        CancellationToken ct
    ) =>
        executor.RunObjectAsync(
            parseResult,
            commandName,
            (client, c) => ExampleAsync(listIds(client), getRaw(client), kind, c),
            ct
        );

    /// <summary>
    /// Reads the <c>--json-body</c> source and parses it, failing with a message that says which
    /// input was bad rather than a bare parser exception.
    /// </summary>
    /// <param name="body">The body option handle.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed body.</returns>
    /// <exception cref="InvalidOperationException">The input is not valid JSON.</exception>
    public static async Task<JsonNode> ReadBodyAsync(
        JsonBodyOption body,
        ParseResult parseResult,
        CancellationToken ct
    ) =>
        JsonNode.Parse(await body.ReadAsync(parseResult, ct))
        ?? throw new InvalidOperationException(
            "--json-body did not contain a JSON object. Run with --schema to print a real one."
        );

    /// <summary>
    /// Creates an item from a <c>--json-body</c> and reports its id (#204, #218). The create
    /// endpoints return no body, so the id is settled <b>before</b> the POST and written into the
    /// body (Umbraco 14+ honours a client-supplied id): <c>--id</c> when given, else the body's own
    /// <c>id</c>, else a new one. Deciding it up front, rather than reading the <c>Location</c>
    /// header afterwards, is also what makes <c>--id</c> work with a body at all.
    /// </summary>
    /// <param name="body">The parsed body; its <c>id</c> is set in place.</param>
    /// <param name="id">The <c>--id</c> value, or null.</param>
    /// <param name="create">The raw create call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created item's id, name and alias, or the create's failure.</returns>
    /// <exception cref="InvalidOperationException">
    /// The body is not a JSON object, its <c>id</c> is not a UUID, or it differs from <c>--id</c>.
    /// </exception>
    public static async Task<UmbracoResponse<RawCreated>> CreateAsync(
        JsonNode body,
        Guid? id,
        Func<JsonNode, CancellationToken, Task<UmbracoResponse<Empty>>> create,
        CancellationToken ct
    )
    {
        if (body is not JsonObject obj)
            throw new InvalidOperationException(
                "--json-body did not contain a JSON object. Run with --schema to print a real one."
            );

        var bodyId = obj["id"] switch
        {
            null => (Guid?)null,
            JsonValue v when v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) => g,
            var other => throw new InvalidOperationException(
                $"The --json-body id {other.ToJsonString()} is not a UUID."
            ),
        };
        // Two different ids is a contradiction; picking either would create the item somewhere
        // the caller did not expect.
        if (id is { } flag && bodyId is { } fromBody && flag != fromBody)
            throw new InvalidOperationException(
                $"--id {flag} does not match the id {fromBody} in --json-body. Give one, or make them agree."
            );

        var resolved = id ?? bodyId ?? Guid.NewGuid();
        obj["id"] = resolved.ToString();

        var created = await create(obj, ct);
        return created.IsSuccess
            ? UmbracoResponse<RawCreated>.Success(
                new RawCreated(resolved, Text(obj, "name"), Text(obj, "alias"))
            )
            : UmbracoResponse<RawCreated>.FailureFrom(created);
    }

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// Reads an existing entity of this kind and returns it as a worked example for
    /// <c>--schema</c>.
    /// </summary>
    /// <param name="listIds">Lists the ids of this kind.</param>
    /// <param name="getRaw">Reads one verbatim by id.</param>
    /// <param name="kind">The noun, for the error when the instance has none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An example body, or a failure explaining why there is none.</returns>
    public static async Task<UmbracoResponse<JsonNode>> ExampleAsync(
        Func<CancellationToken, Task<UmbracoResponse<IReadOnlyList<Guid>>>> listIds,
        Func<Guid, CancellationToken, Task<UmbracoResponse<JsonNode>>> getRaw,
        string kind,
        CancellationToken ct
    )
    {
        var ids = await listIds(ct);
        if (!ids.IsSuccess)
            return UmbracoResponse<JsonNode>.FailureFrom(ids);

        if (ids.Data is not { Count: > 0 } found)
            return UmbracoResponse<JsonNode>.Failure(
                404,
                $"This instance has no {kind} to use as an example. Create one first, or write "
                    + "the body against the Umbraco Management API reference."
            );

        // The lowest id, so the same instance prints the same example every run - "whatever the
        // API returned first" is not reproducible, and --schema output gets diffed.
        return await getRaw(found.Order().First(), ct);
    }
}
