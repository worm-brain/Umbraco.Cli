using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Umbraco.Cli.Client;

/// <summary>
/// Converts arbitrary JSON (a <see cref="JsonElement"/>) into the Kiota
/// <see cref="UntypedNode"/> tree required by the generated content/media/member request
/// models (their <c>Value</c> properties are typed as <see cref="UntypedNode"/>, not plain
/// objects). Property-editor values in Umbraco are arbitrarily shaped JSON - a string, a
/// number, a boolean, an array, or a nested object (block-list editors store large
/// structures) - so the conversion is fully recursive.
/// </summary>
/// <remarks>
/// Kiota exposes no public <see cref="JsonElement"/> -&gt; <see cref="UntypedNode"/> factory,
/// so this is hand-written. Objects, arrays, strings, booleans and null map losslessly.
/// Numbers are the only ambiguous case: JSON numbers are untyped but an
/// <see cref="UntypedNode"/> must carry a concrete .NET value, and Kiota re-serializes from
/// that value onto the wire. We map by narrowest faithful type - <see cref="int"/> then
/// <see cref="long"/> then <see cref="decimal"/> - so common Umbraco payloads round-trip
/// without precision loss (<see cref="decimal"/> avoids the rounding a <see cref="double"/>
/// would introduce for money-like values). See ADR 0004.
/// </remarks>
public static class UntypedNodeFactory
{
    /// <summary>
    /// Converts a boxed property value into an <see cref="UntypedNode"/>. Property values
    /// reach the client either already boxed as a <see cref="JsonElement"/> (when a request
    /// was deserialized from a <c>--json-body</c>) or as a raw CLR value (when built in
    /// code); both are handled uniformly.
    /// </summary>
    /// <param name="value">The property value: <see langword="null"/>, a
    /// <see cref="JsonElement"/>, or any JSON-serializable CLR object.</param>
    /// <returns>The equivalent <see cref="UntypedNode"/> tree. A <see langword="null"/> value
    /// yields an <see cref="UntypedNull"/>.</returns>
    public static UntypedNode FromValue(object? value)
    {
        return value switch
        {
            null => new UntypedNull(),
            JsonElement element => FromJsonElement(element),
            // Any other CLR value is normalised through the JSON model first, so the same
            // recursive mapping applies regardless of how the value was constructed.
            _ => FromJsonElement(JsonSerializer.SerializeToElement(value)),
        };
    }

    /// <summary>
    /// Recursively converts a <see cref="JsonElement"/> into its <see cref="UntypedNode"/>
    /// equivalent.
    /// </summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>The equivalent <see cref="UntypedNode"/>: <see cref="UntypedObject"/>,
    /// <see cref="UntypedArray"/>, <see cref="UntypedString"/>, one of the numeric node
    /// types, <see cref="UntypedBoolean"/>, or <see cref="UntypedNull"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The element has a
    /// <see cref="JsonValueKind"/> the converter does not recognise.</exception>
    public static UntypedNode FromJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // Preserve property order and keys; recurse into each value.
                var members = new Dictionary<string, UntypedNode>();
                foreach (var property in element.EnumerateObject())
                    members[property.Name] = FromJsonElement(property.Value);
                return new UntypedObject(members);

            case JsonValueKind.Array:
                // Recurse into each element, preserving order.
                var items = new List<UntypedNode>();
                foreach (var item in element.EnumerateArray())
                    items.Add(FromJsonElement(item));
                return new UntypedArray(items);

            case JsonValueKind.String:
                return new UntypedString(element.GetString());

            case JsonValueKind.Number:
                return FromNumber(element);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return new UntypedBoolean(element.GetBoolean());

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return new UntypedNull();

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(element),
                    element.ValueKind,
                    "Unsupported JSON value kind."
                );
        }
    }

    /// <summary>
    /// Maps a JSON number to the narrowest faithful numeric <see cref="UntypedNode"/>:
    /// an integer fitting <see cref="int"/> becomes an <see cref="UntypedInteger"/>, a larger
    /// integer an <see cref="UntypedLong"/>, and a non-integer an <see cref="UntypedDecimal"/>
    /// (falling back to <see cref="UntypedDouble"/> only for values outside
    /// <see cref="decimal"/>'s range). See ADR 0004 for the rationale.
    /// </summary>
    /// <param name="element">A JSON element whose kind is <see cref="JsonValueKind.Number"/>.</param>
    /// <returns>The narrowest numeric node that preserves the value.</returns>
    private static UntypedNode FromNumber(JsonElement element)
    {
        if (element.TryGetInt32(out var i))
            return new UntypedInteger(i);
        if (element.TryGetInt64(out var l))
            return new UntypedLong(l);
        if (element.TryGetDecimal(out var m))
            return new UntypedDecimal(m);
        // Out of decimal's range (e.g. very large exponents); double is the last resort.
        return new UntypedDouble(element.GetDouble());
    }

    /// <summary>
    /// Converts an <see cref="UntypedNode"/> back into a <see cref="JsonNode"/>, the inverse of
    /// <see cref="FromValue"/>.
    /// <para>
    /// Needed because property values arrive from the generated client as <c>UntypedNode</c> and,
    /// until #187 Phase 3, were simply dropped rather than surfaced - so <c>content get</c> could
    /// not show what a document actually held (#168), <c>data-types get</c> could not show a
    /// dropdown's items (#170), and <c>media get</c> could not show a file's dimensions (#172).
    /// </para>
    /// </summary>
    /// <param name="node">The node to convert; null yields a JSON null.</param>
    /// <returns>The equivalent <see cref="JsonNode"/>, or null for a JSON null.</returns>
    public static JsonNode? ToJsonNode(UntypedNode? node) =>
        node switch
        {
            null or UntypedNull => null,
            UntypedObject o => ToJsonObject(o),
            UntypedArray a => new JsonArray([.. a.GetValue().Select(ToJsonNode)]),
            UntypedString s when s.GetValue() is { } v => JsonValue.Create(v),
            UntypedBoolean b => JsonValue.Create(b.GetValue()),
            UntypedInteger i => JsonValue.Create(i.GetValue()),
            UntypedLong l => JsonValue.Create(l.GetValue()),
            UntypedDecimal m => JsonValue.Create(m.GetValue()),
            UntypedDouble d => JsonValue.Create(d.GetValue()),
            UntypedFloat f => JsonValue.Create(f.GetValue()),
            // A node type the generator added since: fall back to its serialized form rather than
            // dropping the value silently, which is the defect this method exists to fix.
            _ => JsonValue.Create(node.ToString()),
        };

    /// <summary>Converts an untyped object node into a <see cref="JsonObject"/>.</summary>
    /// <param name="node">The object node.</param>
    /// <returns>The equivalent JSON object.</returns>
    private static JsonObject ToJsonObject(UntypedObject node)
    {
        var result = new JsonObject();
        foreach (var (key, value) in node.GetValue())
            result[key] = ToJsonNode(value);
        return result;
    }
}
