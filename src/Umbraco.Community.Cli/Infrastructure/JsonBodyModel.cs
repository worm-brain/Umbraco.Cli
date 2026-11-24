using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Umbraco.Community.Cli.Client;
using Umbraco.Community.Cli.Commands;

namespace Umbraco.Community.Cli.Infrastructure;

/// <summary>
/// Reads a <c>--json-body</c> into the typed request a command sends (content, media and document
/// blueprint writes), turning a wrong-typed field into a message in the caller's terms (#459).
/// <para>
/// System.Text.Json reports a mismatch as "could not be converted to
/// Umbraco.Community.Cli.Client.ContentTypeReference", which names a CLI-internal type and says
/// nothing about the shape wanted. Here the failing path is walked through the same serializer
/// metadata the read used, so the message names the field and the shape it takes
/// (<c>contentType must be an object such as {"alias": "blogPost"} or {"id": "&lt;guid&gt;"}</c>)
/// and points at <c>--schema</c>, which is generated from that same metadata
/// (<see cref="JsonBodySchema"/>).
/// </para>
/// </summary>
public static partial class JsonBodyModel
{
    // The same options JsonBodySchema exports with, so the shape described here is the shape
    // --schema prints.
    private static readonly JsonSerializerOptions Options = JsonSerializerOptions.Default;

    /// <summary>
    /// The shapes of the CLI's reference types, written out as examples: their keys alone
    /// (<c>id</c>, <c>alias</c>) would not say that either one is enough.
    /// </summary>
    private static readonly Dictionary<Type, string> ReferenceShapes = new()
    {
        [typeof(ContentTypeReference)] =
            """an object such as {"alias": "blogPost"} or {"id": "<guid>"}""",
        [typeof(ContentTemplateReference)] =
            """an object such as {"alias": "articlePage"} or {"id": "<guid>"}""",
        [typeof(ContentParentReference)] = """an object such as {"id": "<guid>"}""",
    };

    /// <summary>Parses a <c>--json-body</c> and reads it as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="json">The body text.</param>
    /// <returns>The request, or null for a literal <c>null</c> body.</returns>
    /// <exception cref="JsonException">The text is not valid JSON (the parser's message, which names no type).</exception>
    /// <exception cref="InvalidInputException">A field has the wrong type or a required key is missing.</exception>
    public static T? Deserialize<T>(string json) => Deserialize<T>(JsonNode.Parse(json));

    /// <summary>Reads a parsed <c>--json-body</c> as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="body">The parsed body; null for a literal <c>null</c>.</param>
    /// <returns>The request, or null for a null body.</returns>
    /// <exception cref="InvalidInputException">A field has the wrong type or a required key is missing.</exception>
    public static T? Deserialize<T>(JsonNode? body)
    {
        try
        {
            return body.Deserialize<T>(Options);
        }
        catch (JsonException ex)
        {
            // The body is already parsed, so every JsonException here is a mismatch between the
            // body and the model; the serializer's own message is never shown.
            throw new InvalidInputException(Explain(typeof(T), body, ex.Path));
        }
    }

    /// <summary>
    /// Explains a deserialisation failure at <paramref name="path"/> in user terms: a required key
    /// missing there, or the value there having the wrong type.
    /// </summary>
    /// <param name="root">The request type.</param>
    /// <param name="body">The body.</param>
    /// <param name="path">The serializer's JSON path of the failure (<c>$.variants[0].name</c>), if it gave one.</param>
    /// <returns>The message, naming no .NET type.</returns>
    internal static string Explain(Type root, JsonNode? body, string? path)
    {
        const string Hint = " Run with --schema to see the body's shape.";
        var segments = Segments(path ?? "$");
        var (type, node) = Walk(Options.GetTypeInfo(root), body, segments);
        var field = Display(segments);

        if (type is null)
            return $"--json-body {field} has the wrong type.{Hint}";

        // A missing required key fails on its parent object, so the path points there.
        if (type.Kind == JsonTypeInfoKind.Object && node is JsonObject obj)
        {
            var missing = type
                .Properties.Where(p => p.IsRequired && !obj.ContainsKey(p.Name))
                .ToList();
            if (missing.Count > 0)
                return string.Join(
                        " ",
                        missing.Select(p =>
                            $"--json-body needs {Join(segments, p.Name)}, {Shape(Options.GetTypeInfo(p.PropertyType))}."
                        )
                    ) + Hint;
        }

        return $"--json-body {field} must be {Shape(type)}{Given(node)}.{Hint}";
    }

    /// <summary>
    /// Follows <paramref name="segments"/> down the type metadata and the body together.
    /// </summary>
    /// <param name="type">The root type's metadata.</param>
    /// <param name="node">The body.</param>
    /// <param name="segments">Property names, and array indexes as ints.</param>
    /// <returns>The metadata and the body node at the path; a null type when the path leaves the model.</returns>
    private static (JsonTypeInfo? Type, JsonNode? Node) Walk(
        JsonTypeInfo type,
        JsonNode? node,
        List<object> segments
    )
    {
        JsonTypeInfo? current = type;
        foreach (var segment in segments)
        {
            current = (segment, current) switch
            {
                (string name, { Kind: JsonTypeInfoKind.Object } t) => t.Properties.FirstOrDefault(
                    p => p.Name == name
                )
                    is { } property
                    ? Options.GetTypeInfo(property.PropertyType)
                    : null,
                (int, { Kind: JsonTypeInfoKind.Enumerable, ElementType: { } element }) =>
                    Options.GetTypeInfo(element),
                _ => null,
            };
            node = (segment, node) switch
            {
                (string name, JsonObject o) => o[name],
                (int index, JsonArray a) when index < a.Count => a[index],
                _ => null,
            };
            if (current is null)
                return (null, null);
        }
        return (current, node);
    }

    /// <summary>The shape a value of <paramref name="type"/> takes, in JSON terms.</summary>
    /// <param name="type">The value's metadata.</param>
    /// <returns>For example <c>a string</c>, or <c>an array, each item an object with the keys ...</c>.</returns>
    private static string Shape(JsonTypeInfo type)
    {
        var clr = Nullable.GetUnderlyingType(type.Type) ?? type.Type;
        if (ReferenceShapes.TryGetValue(clr, out var reference))
            return reference;
        if (clr == typeof(Guid))
            return "an id, as a string such as \"3f7a8b2e-1234-5678-abcd-ef0123456789\"";
        if (clr == typeof(string))
            return "a string";
        if (clr == typeof(bool))
            return "true or false";
        if (IsNumber(clr))
            return "a number";
        return type.Kind switch
        {
            JsonTypeInfoKind.Enumerable when type.ElementType is { } element =>
                $"an array, each item {Shape(Options.GetTypeInfo(element))}",
            JsonTypeInfoKind.Object when type.Properties.Count > 0 =>
                $"an object with the keys {Join(type.Properties.Select(p => p.Name))}",
            JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary => "an object",
            _ => "a JSON value of another type",
        };
    }

    /// <summary>What the body actually had at the path, for the end of the message.</summary>
    /// <param name="node">The node at the path.</param>
    /// <returns>
    /// <c>, not a string</c> and the like, or nothing when unknown. A string is quoted instead
    /// (<c>, not "probe"</c>), since a string is also what an id takes.
    /// </returns>
    private static string Given(JsonNode? node) =>
        node?.GetValueKind() switch
        {
            JsonValueKind.String when node.GetValue<string>() is { Length: <= 60 } text =>
                $", not \"{text}\"",
            JsonValueKind.String => ", not that string",
            JsonValueKind.Number => ", not a number",
            JsonValueKind.True or JsonValueKind.False => ", not true or false",
            JsonValueKind.Array => ", not an array",
            JsonValueKind.Object => ", not an object",
            _ => "",
        };

    private static bool IsNumber(Type type) =>
        type == typeof(int)
        || type == typeof(long)
        || type == typeof(short)
        || type == typeof(decimal)
        || type == typeof(double)
        || type == typeof(float);

    /// <summary>
    /// Splits a serializer path (<c>$.variants[0]['odd key']</c>) into property names (strings)
    /// and array indexes (ints).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The segments below the root.</returns>
    private static List<object> Segments(string path) =>
        PathSegment()
            .Matches(path)
            .Select(m =>
                m.Groups["index"].Success
                    ? (object)int.Parse(m.Groups["index"].Value, CultureInfo.InvariantCulture)
                : m.Groups["quoted"].Success ? m.Groups["quoted"].Value
                : m.Groups["name"].Value
            )
            .ToList();

    /// <summary>The field as the message shows it: <c>contentType</c>, <c>variants[0].name</c>, or <c>as a whole</c> for the root.</summary>
    /// <param name="segments">The path segments.</param>
    /// <returns>The display text.</returns>
    private static string Display(List<object> segments) =>
        segments.Count == 0 ? "as a whole" : Join(segments, null);

    /// <summary>Joins path segments, plus an optional last key, as <c>variants[0].name</c>.</summary>
    /// <param name="segments">The path segments.</param>
    /// <param name="last">A key to add at the end, or null.</param>
    /// <returns>The joined path.</returns>
    private static string Join(IEnumerable<object> segments, string? last)
    {
        var text = "";
        foreach (var segment in last is null ? segments : segments.Append(last))
            text +=
                segment is int i ? $"[{i}]"
                : text.Length == 0 ? (string)segment
                : $".{segment}";
        return text;
    }

    /// <summary>Joins key names for a message: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    /// <param name="names">The names.</param>
    /// <returns>The joined text.</returns>
    private static string Join(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count <= 1
            ? string.Concat(list)
            : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    [GeneratedRegex(@"\.(?<name>[^.\[]+)|\['(?<quoted>(?:[^'\\]|\\.)*)'\]|\[(?<index>\d+)\]")]
    private static partial Regex PathSegment();
}
