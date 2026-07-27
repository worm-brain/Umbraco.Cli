using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Umbraco.Cli.Infrastructure;

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

    /// <summary>
    /// Builds the JSON Schema node for <typeparamref name="TBody"/>.
    /// </summary>
    /// <typeparam name="TBody">The <c>--json-body</c> request type.</typeparam>
    /// <returns>The schema as a <see cref="JsonNode"/>.</returns>
    public static JsonNode For<TBody>() =>
        JsonSchemaExporter.GetJsonSchemaAsNode(BodyOptions, typeof(TBody));

    /// <summary>
    /// Prints the JSON Schema for <typeparamref name="TBody"/> to stdout as an indented JSON
    /// Schema document (not wrapped in the CLI envelope — a schema is itself a standard document
    /// an agent can feed straight to a validator).
    /// </summary>
    /// <typeparam name="TBody">The <c>--json-body</c> request type.</typeparam>
    public static void Print<TBody>()
    {
        var schema = For<TBody>();
        Console.WriteLine(schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
