using System.Text;
using System.Text.Json.Nodes;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Shared shaping used by every structured output writer (#123): turning a <c>WriteTable</c>
/// (headers + rows) into records keyed by camelCase field names, and applying the <c>--fields</c>
/// projection (#63). Centralising this keeps list output and get/object output agreeing on field
/// keys across all formats (the fix behind #87) instead of each writer re-deriving its own.
/// </summary>
public static class OutputShaping
{
    /// <summary>
    /// Converts a table's headers + rows into records keyed by the camelCased header names, so
    /// tabular output uses the same field keys as object output. Throws if two headers collide on
    /// their camelCase key (which would silently drop a column) so a future clash fails in tests.
    /// </summary>
    /// <param name="headers">The human-readable column headers.</param>
    /// <param name="rows">The row values, aligned to <paramref name="headers"/>.</param>
    /// <returns>One insertion-ordered record per row.</returns>
    public static IEnumerable<IReadOnlyDictionary<string, string>> TableToRecords(
        string[] headers,
        IEnumerable<string[]> rows
    )
    {
        var keys = headers.Select(ToCamelKey).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new InvalidOperationException(
                $"Table headers produced duplicate field keys ({string.Join(", ", headers)}). "
                    + "Rename a header so each column's camelCase key is unique."
            );
        return rows.Select(row =>
        {
            var dict = new Dictionary<string, string>();
            for (var i = 0; i < keys.Length && i < row.Length; i++)
                dict[keys[i]] = row[i];
            return (IReadOnlyDictionary<string, string>)dict;
        });
    }

    /// <summary>
    /// Converts a human table header to a camelCase field key (#87): "ID" -&gt; "id", "Content Type"
    /// -&gt; "contentType", "Version ID" -&gt; "versionId". Words split on whitespace; all-caps
    /// acronyms are title-cased before the first word is lower-cased and the rest Pascal-cased.
    /// </summary>
    /// <param name="header">The human-readable column header.</param>
    /// <returns>The camelCase key for that column.</returns>
    public static string ToCamelKey(string header)
    {
        var words = header.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (words.Length == 0)
            return header;

        var sb = new StringBuilder(header.Length);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            // Collapse an all-caps acronym ("ID") to title case ("Id") so casing is predictable.
            if (w.All(char.IsUpper))
                w = char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
            // First word is lower-cased (camel); subsequent words are Pascal-cased.
            w =
                i == 0
                    ? char.ToLowerInvariant(w[0]) + w[1..]
                    : char.ToUpperInvariant(w[0]) + w[1..];
            sb.Append(w);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Projects a data node to the requested <paramref name="fields"/> (#63): an array projects each
    /// element; an object keeps only the named fields, in request order, matched case-insensitively
    /// (so <c>--fields contentType</c> also selects a table's "Content Type"). Non-object nodes and a
    /// null/empty <paramref name="fields"/> are returned unchanged.
    /// </summary>
    /// <param name="node">The serialized data node.</param>
    /// <param name="fields">The field names to keep, in order, or null for no projection.</param>
    /// <returns>The projected node.</returns>
    public static JsonNode? Project(JsonNode? node, string[]? fields)
    {
        if (fields is not { Length: > 0 })
            return node;
        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
                result.Add(ProjectObject(item, fields));
            return result;
        }
        return ProjectObject(node, fields);
    }

    private static JsonNode? ProjectObject(JsonNode? node, string[] fields)
    {
        if (node is not JsonObject obj)
            return node?.DeepClone();

        var result = new JsonObject();
        foreach (var field in fields)
        {
            var match = obj.FirstOrDefault(kv => kv.Key == field) is { Key: not null } exact
                ? exact
                : obj.FirstOrDefault(kv => NormalizeKey(kv.Key) == NormalizeKey(field));
            if (match.Key is not null && !result.ContainsKey(match.Key))
                result[match.Key] = match.Value?.DeepClone();
        }
        return result;
    }

    /// <summary>Lower-cases and strips whitespace so "Content Type" and "contentType" match.</summary>
    private static string NormalizeKey(string key) =>
        new string(key.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
}
