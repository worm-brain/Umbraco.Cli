using System.Text.Json.Nodes;
using Umbraco.Community.Cli.Commands;

namespace Umbraco.Community.Cli.Infrastructure;

/// <summary>
/// Finds the keys in a <c>--json-body</c> that its JSON Schema does not define, at any depth, so a
/// misspelt key or a wrong nested shape fails the command instead of being silently dropped
/// (ADR 0011). Every <c>--json-body</c> that is checked goes through here: the schema nouns'
/// Management API bodies (<see cref="ApiBodySchema"/>) and the typed content, media and blueprint
/// bodies (<see cref="JsonBodyModel"/>).
/// <para>
/// A body is checked against one or more schema documents at once, and a key is unknown only
/// where none of them defines it. That is how the output of <c>get</c> stays a valid body: its
/// shape is passed alongside the request's, so the read-only keys it carries are accepted where
/// <c>get</c> prints them and nowhere else. Only keys are checked, never values.
/// </para>
/// </summary>
public static class JsonBodyKeys
{
    /// <summary>
    /// Finds every key in <paramref name="body"/> that none of <paramref name="schemas"/> defines
    /// at its position. An object is checked only where every schema describing it is closed
    /// (<c>additionalProperties: false</c>); below an open or free-form schema (a property value
    /// typed as any JSON) nothing is reported.
    /// </summary>
    /// <param name="body">The <c>--json-body</c>.</param>
    /// <param name="schemas">
    /// Whole JSON Schema documents. A <c>$ref</c> is resolved as a JSON Pointer within the document
    /// it appears in (<c>#/$defs/X</c>, <c>#/properties/values/items</c>). The structure keywords
    /// followed are <c>$ref</c>, <c>oneOf</c> (one model, optionally or-null), <c>properties</c>,
    /// <c>additionalProperties</c> and <c>items</c>, which is all the CLI's schemas use.
    /// </param>
    /// <returns>
    /// The JSON path of each unknown key in document order (<c>$.allowedTemplates[0].template</c>),
    /// or an empty list when every key is known.
    /// </returns>
    public static IReadOnlyList<string> FindUnknown(JsonNode body, params JsonNode[] schemas)
    {
        var unknown = new List<string>();
        Walk(body, [.. schemas.Select(s => new Candidate(s, s))], "$", unknown);
        return unknown;
    }

    /// <summary>
    /// The <c>invalid_argument</c> error for unknown keys, worded the same on every command.
    /// </summary>
    /// <param name="unknown">The unknown keys' paths, from <see cref="FindUnknown"/>.</param>
    /// <param name="schema">Which schema they are missing from, e.g. <c>the document type update schema</c>.</param>
    /// <param name="ignoredBy">Who would drop them: <c>Umbraco</c>, or <c>the CLI</c> for a typed body.</param>
    /// <param name="allowEffect">What <c>--allow-unknown-keys</c> does instead, e.g. <c>send it as it is</c>.</param>
    /// <returns>The exception to throw.</returns>
    public static InvalidInputException Error(
        IReadOnlyList<string> unknown,
        string schema,
        string ignoredBy,
        string allowEffect
    )
    {
        var one = unknown.Count == 1;
        return new InvalidInputException(
            $"--json-body has {(one ? "a key" : "keys")} {schema} does not define: "
                + $"{string.Join(", ", unknown)}. {ignoredBy} would ignore {(one ? "it" : "them")}. "
                + $"Run with --schema to see the body's shape, or pass --allow-unknown-keys to {allowEffect}."
        );
    }

    /// <summary>A schema at some position, with the document its <c>$ref</c>s resolve in.</summary>
    /// <param name="Schema">The schema.</param>
    /// <param name="Document">The whole document.</param>
    private readonly record struct Candidate(JsonNode Schema, JsonNode Document);

    /// <summary>Checks one value against every schema that describes its position.</summary>
    /// <param name="value">The value; null for a JSON null.</param>
    /// <param name="candidates">The schemas describing it.</param>
    /// <param name="path">The value's JSON path.</param>
    /// <param name="unknown">Collects the unknown keys' paths.</param>
    private static void Walk(
        JsonNode? value,
        List<Candidate> candidates,
        string path,
        List<string> unknown
    )
    {
        if (value is null or JsonValue)
            return;

        var resolved = new List<Candidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            // A boolean schema (true) or a choice of several models says nothing about the keys
            // here: any of them may be legitimate, so nothing below is reported.
            if (Resolve(candidate) is not { } schema)
                return;
            resolved.Add(schema);
        }

        switch (value)
        {
            case JsonObject obj:
                if (!resolved.All(IsClosedObject))
                    return;
                foreach (var (key, child) in obj)
                {
                    var children = resolved
                        .Where(c => c.Schema["properties"]![key] is not null)
                        .Select(c => c with { Schema = c.Schema["properties"]![key]! })
                        .ToList();
                    if (children.Count == 0)
                        unknown.Add(Child(path, key));
                    else
                        Walk(child, children, Child(path, key), unknown);
                }
                break;
            case JsonArray array:
                if (resolved.Any(c => c.Schema["items"] is null))
                    return;
                var items = resolved.Select(c => c with { Schema = c.Schema["items"]! }).ToList();
                for (var i = 0; i < array.Count; i++)
                    Walk(array[i], items, $"{path}[{i}]", unknown);
                break;
        }
    }

    /// <summary>
    /// Follows <c>$ref</c>s and a single-model <c>oneOf</c> to the schema that describes a value.
    /// </summary>
    /// <param name="candidate">The schema and its document.</param>
    /// <returns>The concrete schema, or null when it does not constrain keys (a boolean schema, or a choice of models).</returns>
    private static Candidate? Resolve(Candidate candidate)
    {
        var schema = candidate.Schema;
        while (true)
        {
            if (schema is not JsonObject obj)
                return null;
            if (obj["$ref"] is JsonValue reference)
            {
                schema = Pointer(candidate.Document, reference.GetValue<string>());
                continue;
            }
            if (obj["oneOf"] is JsonArray oneOf)
            {
                var models = oneOf
                    .OfType<JsonObject>()
                    .Where(o => o["type"]?.GetValue<string>() != "null")
                    .ToList();
                if (models.Count != 1)
                    return null;
                schema = models[0];
                continue;
            }
            return candidate with { Schema = obj };
        }
    }

    /// <summary>Whether a schema lists its keys and allows no others.</summary>
    /// <param name="candidate">A resolved schema.</param>
    /// <returns>True for a closed object schema.</returns>
    private static bool IsClosedObject(Candidate candidate) =>
        candidate.Schema["properties"] is JsonObject
        && candidate.Schema["additionalProperties"] is JsonValue additional
        && additional.TryGetValue<bool>(out var allowed)
        && !allowed;

    /// <summary>Resolves a local JSON Pointer reference (<c>#/a/b</c>) against a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="reference">The reference.</param>
    /// <returns>The node it points at, or null when it points nowhere.</returns>
    private static JsonNode? Pointer(JsonNode document, string reference)
    {
        JsonNode? node = document;
        foreach (
            var token in reference
                .TrimStart('#')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(t =>
                    t.Replace("~1", "/", StringComparison.Ordinal)
                        .Replace("~0", "~", StringComparison.Ordinal)
                )
        )
            node = node switch
            {
                JsonObject o => o[token],
                JsonArray a
                    when int.TryParse(
                        token,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var i
                    )
                        && i < a.Count => a[i],
                _ => null,
            };
        return node;
    }

    /// <summary>
    /// A child key's JSON path: <c>$.name</c>, or <c>$['odd key']</c> when the key is not a plain
    /// identifier, so a key with a dot or a space in it reads unambiguously.
    /// </summary>
    /// <param name="path">The parent's path.</param>
    /// <param name="key">The key.</param>
    /// <returns>The child's path.</returns>
    private static string Child(string path, string key) =>
        key.Length > 0
        && (char.IsAsciiLetter(key[0]) || key[0] is '_' or '$')
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')
            ? $"{path}.{key}"
            : $"{path}['{key.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";
}
