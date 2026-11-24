using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Umbraco.Community.Cli.Infrastructure;

/// <summary>
/// Generates the JSON Schema for a command's <c>--json-body</c> request type (#61) so an agent
/// can learn the exact shape of the body to send. The schema is derived from the request record
/// itself via .NET's <see cref="JsonSchemaExporter"/>, so it can never drift from the type the
/// command actually deserializes — and it honours the records' <c>[JsonPropertyName]</c>
/// attributes (the wire field names), matching how bodies are parsed.
/// </summary>
public static class JsonBodySchema
{
    // Bodies are deserialized with default options (the records carry [JsonPropertyName]), so the
    // schema is exported with the same options to keep the field names identical.
    private static readonly JsonSerializerOptions BodyOptions = JsonSerializerOptions.Default;

    // Treat the root as non-nullable: the command rejects a literal `null` body, so the schema
    // must not advertise `"type": ["object", "null"]` (that would let a `null` body pass a
    // validator yet fail the command).
    // Every record is closed (additionalProperties: false): a key the record does not define fails
    // the command (ADR 0011), and the schema says so. A free-form value (object, JsonNode) stays
    // open, as it has no properties.
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = (context, node) =>
        {
            if (
                context.TypeInfo.Kind == JsonTypeInfoKind.Object
                && node is JsonObject schema
                && schema.ContainsKey("properties")
            )
                schema["additionalProperties"] = false;
            return node;
        },
    };

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    /// <summary>
    /// Builds the JSON Schema node for <typeparamref name="TBody"/>.
    /// </summary>
    /// <typeparam name="TBody">The <c>--json-body</c> request type.</typeparam>
    /// <returns>The schema as a <see cref="JsonNode"/>.</returns>
    public static JsonNode For<TBody>() => For(typeof(TBody));

    /// <summary>
    /// Builds the JSON Schema node for <paramref name="type"/>: a request type, or a read type
    /// whose output is also accepted as a body (<see cref="JsonBodyModel"/>).
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The schema as a <see cref="JsonNode"/>.</returns>
    public static JsonNode For(Type type) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(BodyOptions, type, ExporterOptions);

    /// <summary>
    /// Prints the JSON Schema for <typeparamref name="TBody"/> to stdout as an indented JSON
    /// Schema document (not wrapped in the CLI envelope — a schema is itself a standard document
    /// an agent can feed straight to a validator).
    /// </summary>
    /// <typeparam name="TBody">The <c>--json-body</c> request type.</typeparam>
    public static void Print<TBody>() =>
        Console.WriteLine(For<TBody>().ToJsonString(IndentedOptions));
}
