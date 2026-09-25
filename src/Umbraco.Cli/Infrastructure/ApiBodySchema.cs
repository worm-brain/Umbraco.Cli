using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The JSON Schema of a raw schema verb's <c>--json-body</c> (document, data, media and member
/// types, templates), printed by <c>--schema</c> without a host (docs/conventions.md 4.1). The
/// documents are generated from the Management API spec into the embedded
/// <c>Schemas/api-bodies.json</c>; a test regenerates them and fails on drift, so they are never
/// a hand-kept second definition.
/// </summary>
public static class ApiBodySchema
{
    private static readonly Lazy<JsonObject> Documents = new(() =>
    {
        using var stream =
            typeof(ApiBodySchema).Assembly.GetManifestResourceStream(
                "Umbraco.Cli.Schemas.api-bodies.json"
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
}
