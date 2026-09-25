using System.Text.Json.Nodes;

namespace Umbraco.Cli.Client;

/// <summary>
/// Turns <c>GET /data-type/{id}/referenced-by</c> into list rows (#247). The endpoint answers a
/// paged model whose items are a mixed set of kinds told apart by a .NET <c>$type</c>
/// discriminator (<c>DocumentTypePropertyTypeReferenceResponseModel</c> and so on), and the CLI
/// used to print that verbatim inside <c>data</c> - the only list that did not use the
/// <c>data: [...]</c> plus <c>meta.total</c> envelope. Each row keeps every field the item had,
/// with <c>$type</c> replaced by a readable <c>kind</c> such as <c>documentTypePropertyType</c>.
/// </summary>
public static class DataTypeReferenceRows
{
    private const string Suffix = "ReferenceResponseModel";

    /// <summary>Maps the raw paged body to rows.</summary>
    /// <param name="raw">The body as read, <c>{ total, items: [...] }</c>.</param>
    /// <returns>The rows, with the server's total.</returns>
    public static PagedResponse<JsonObject> From(JsonNode? raw)
    {
        var items = (raw?["items"] as JsonArray ?? []).OfType<JsonObject>().Select(Row).ToList();
        var total =
            raw?["total"] is JsonValue t && t.TryGetValue<long>(out var n) ? (int)n : items.Count;
        return new PagedResponse<JsonObject> { Total = total, Items = items };
    }

    /// <summary>Copies one item, replacing <c>$type</c> with <c>kind</c>.</summary>
    /// <param name="item">The raw item.</param>
    /// <returns>The row.</returns>
    private static JsonObject Row(JsonObject item)
    {
        var row = new JsonObject();
        if (item["$type"]?.GetValue<string?>() is { Length: > 0 } type)
            row["kind"] = Kind(type);
        foreach (var (key, value) in item)
            if (key != "$type")
                row[key] = value?.DeepClone();
        return row;
    }

    /// <summary><c>DocumentTypePropertyTypeReferenceResponseModel</c> becomes <c>documentTypePropertyType</c>.</summary>
    /// <param name="type">The discriminator.</param>
    /// <returns>The kind.</returns>
    internal static string Kind(string type)
    {
        var name = type.EndsWith(Suffix, StringComparison.Ordinal) ? type[..^Suffix.Length] : type;
        return name.Length == 0 ? type : char.ToLowerInvariant(name[0]) + name[1..];
    }
}
