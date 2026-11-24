using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Community.Cli.Client;

namespace Umbraco.Community.Cli.Infrastructure;

/// <summary>
/// The JSON Schema of a raw schema verb's <c>--json-body</c> (document, data, media and member
/// types, templates), printed by <c>--schema</c> without a host (docs/conventions.md 4.1). The
/// documents are generated from the Management API spec into the embedded
/// <c>Schemas/api-bodies.json</c>; a test regenerates them and fails on drift, so they are never
/// a hand-kept second definition. The same documents check a body's keys before it is sent
/// (<see cref="FindUnknownKeys"/>).
/// </summary>
public static class ApiBodySchema
{
    private static readonly Lazy<JsonObject> Documents = new(() =>
    {
        using var stream =
            typeof(ApiBodySchema).Assembly.GetManifestResourceStream(
                "Umbraco.Community.Cli.Schemas.api-bodies.json"
            ) ?? throw new InvalidOperationException("The embedded API body schemas are missing.");
        return JsonNode.Parse(stream)!.AsObject();
    });

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The Management API request model a kind's create or update body is.</summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="update">True for the update body, false for create.</param>
    /// <returns>The model name in the spec.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind has no raw body for that verb.</exception>
    public static string ModelFor(EntityKind kind, bool update) =>
        (kind, update) switch
        {
            (EntityKind.DocumentType, false) => "CreateDocumentTypeRequestModel",
            (EntityKind.DocumentType, true) => "UpdateDocumentTypeRequestModel",
            (EntityKind.DataType, false) => "CreateDataTypeRequestModel",
            (EntityKind.DataType, true) => "UpdateDataTypeRequestModel",
            (EntityKind.MediaType, false) => "CreateMediaTypeRequestModel",
            (EntityKind.MediaType, true) => "UpdateMediaTypeRequestModel",
            (EntityKind.MemberType, false) => "CreateMemberTypeRequestModel",
            (EntityKind.MemberType, true) => "UpdateMemberTypeRequestModel",
            (EntityKind.Template, true) => "UpdateTemplateRequestModel",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                $"{kind} has no raw {(update ? "update" : "create")} body."
            ),
        };

    /// <summary>The JSON Schema document for a kind's create or update body.</summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="update">True for the update body (every key optional: it is merged), false for create.</param>
    /// <returns>A fresh copy of the document.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind has no raw body for that verb.</exception>
    public static JsonNode For(EntityKind kind, bool update) =>
        Documents.Value[ModelFor(kind, update)]!.DeepClone();

    /// <summary>Prints the schema to stdout, bare (not in the envelope), as <c>content create --schema</c> does.</summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="update">True for the update body, false for create.</param>
    public static void Print(EntityKind kind, bool update) =>
        Console.WriteLine(For(kind, update).ToJsonString(Indented));

    /// <summary>
    /// The top-level keys a kind's <c>get</c> returns that neither request model defines: the
    /// item's own <c>id</c> (absent from the update models) and the server's read-only flags. A
    /// body edited from <c>get</c> or <c>--example</c> output carries them, and the server has
    /// always ignored them, so <see cref="FindUnknownKeys"/> accepts them (ADR 0011). They are the
    /// response model's properties minus the update model's in <c>spec/management.json</c>; a test
    /// recomputes them from the spec. Nested models have no such extras.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <returns>The key names.</returns>
    internal static IReadOnlySet<string> ReadOnlyKeys(EntityKind kind) =>
        kind switch
        {
            EntityKind.MediaType => new HashSet<string>
            {
                "id",
                "isDeletable",
                "aliasCanBeChanged",
            },
            EntityKind.DataType => new HashSet<string>
            {
                "id",
                "isDeletable",
                "canIgnoreStartNodes",
            },
            EntityKind.Template => new HashSet<string> { "id", "masterTemplate" },
            _ => new HashSet<string> { "id" },
        };

    /// <summary>
    /// Finds every key in <paramref name="body"/> that the kind's create or update schema does not
    /// define, at any depth, so a misspelt key or a wrong nested shape fails instead of being
    /// silently dropped by the server (#460, ADR 0011). Only keys are checked, never values: a
    /// value of the wrong type or format is the server's to reject.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <param name="update">True to check against the update schema, false for create.</param>
    /// <param name="body">The <c>--json-body</c>.</param>
    /// <returns>
    /// The JSON path of each unknown key in document order (<c>$.allowedTemplates[0].template</c>),
    /// or an empty list when every key is known.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind has no raw body for that verb.</exception>
    public static IReadOnlyList<string> FindUnknownKeys(EntityKind kind, bool update, JsonNode body)
    {
        // The read-only keys are a second, top-level-only schema beside the request's: each one
        // is accepted (as any value) at the top level and nowhere else.
        var readOnly = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(
                ReadOnlyKeys(kind).Select(k => KeyValuePair.Create<string, JsonNode?>(k, true))
            ),
            ["additionalProperties"] = false,
        };
        return JsonBodyKeys.FindUnknown(body, Documents.Value[ModelFor(kind, update)]!, readOnly);
    }
}
