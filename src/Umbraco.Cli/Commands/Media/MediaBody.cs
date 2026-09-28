using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// What a media body means to a promotion (#226): the file an item holds, and the part of the body
/// that two instances holding the same item agree on.
/// <para>
/// An item's file lives in its <c>umbracoFile</c> value: <c>{src}</c> for an upload field,
/// <c>{src, crops, focalPoint}</c> for an image cropper. <c>src</c> carries a per-upload folder
/// (<c>/media/&lt;random&gt;/name.ext</c>), so it differs on every instance even for the same file,
/// and <c>umbracoBytes</c>, <c>umbracoWidth</c>, <c>umbracoHeight</c> and <c>umbracoExtension</c>
/// are computed by the server from the file. The file is compared on its own (name, size, and
/// optionally hash), so those are left out of the body comparison.
/// </para>
/// </summary>
public static class MediaBody
{
    /// <summary>The alias of the property that holds a media item's file.</summary>
    public const string FileAlias = "umbracoFile";

    /// <summary>Values the server fills in from the file.</summary>
    private static readonly string[] AutoFilled =
    [
        "umbracoBytes",
        "umbracoWidth",
        "umbracoHeight",
        "umbracoExtension",
    ];

    /// <summary>The parts of a file value that name one stored copy rather than the file.</summary>
    private static readonly string[] FileInstanceFields = ["src", "temporaryFileId"];

    private static readonly string[] TopLevelNoise = ["isTrashed", "flags"];

    private static readonly string[] VariantNoise = ["createDate", "updateDate", "flags", "state"];

    /// <summary>
    /// Returns a normalised copy of <paramref name="body"/> (the input is not mutated): without the
    /// instance's bookkeeping (<c>isTrashed</c>, <c>flags</c>, variant dates), without the
    /// file-derived values and the file's <c>src</c>, and with values and variants sorted.
    /// </summary>
    /// <param name="body">A verbatim media body.</param>
    /// <returns>The normalised clone.</returns>
    public static JsonNode Normalise(JsonNode body)
    {
        var clone = body.DeepClone();
        if (clone is not JsonObject item)
            return clone;

        foreach (var field in TopLevelNoise)
            item.Remove(field);

        if (item["variants"] is JsonArray variants)
        {
            foreach (var variant in variants.OfType<JsonObject>())
            foreach (var field in VariantNoise)
                variant.Remove(field);
            item["variants"] = SnapshotBody.Sorted(
                variants,
                v => (null, SnapshotBody.Text(v, "culture"), SnapshotBody.Text(v, "segment"))
            );
        }

        if (item["values"] is JsonArray values)
        {
            foreach (var value in values.ToList())
            {
                var alias = SnapshotBody.Text(value, "alias");
                if (AutoFilled.Contains(alias))
                    values.Remove(value);
                else if (alias == FileAlias && value?["value"] is JsonObject file)
                    foreach (var field in FileInstanceFields)
                        file.Remove(field);
            }
            item["values"] = SnapshotBody.Sorted(
                values,
                v =>
                    (
                        SnapshotBody.Text(v, "alias"),
                        SnapshotBody.Text(v, "culture"),
                        SnapshotBody.Text(v, "segment")
                    )
            );
        }

        return item;
    }

    /// <summary>The <c>src</c> of the item's file, or null when it holds none.</summary>
    /// <param name="body">A verbatim media body.</param>
    /// <returns>The file's path on the site, or null.</returns>
    public static string? SrcOf(JsonNode? body) =>
        SnapshotBody.Text(FileValueEntry(body)?["value"], "src") is { Length: > 0 } src
            ? src
            : null;

    /// <summary>The file name a <c>src</c> ends in (URL-decoded), e.g. <c>photo.jpg</c>.</summary>
    /// <param name="src">The file's <c>src</c>.</param>
    /// <returns>The file name.</returns>
    public static string FileNameOf(string src)
    {
        // An absolute URL may carry a query string (a cache buster); the path is what names it.
        var path = src.Split('?', '#')[0];
        return Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
    }

    /// <summary>The file size the server recorded (<c>umbracoBytes</c>), or null when absent.</summary>
    /// <param name="body">A verbatim media body.</param>
    /// <returns>The size in bytes, or null.</returns>
    public static long? BytesOf(JsonNode? body)
    {
        var value = (body?["values"] as JsonArray ?? [])
            .FirstOrDefault(v => SnapshotBody.Text(v, "alias") == "umbracoBytes")
            ?["value"];
        if (value is not JsonValue v)
            return null;
        if (v.TryGetValue<long>(out var n))
            return n;
        return v.TryGetValue<string>(out var s) && long.TryParse(s, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// A write body that sets the item's file to a staged temporary file: <paramref name="body"/>
    /// (normalised) with its <c>umbracoFile</c> value set to <c>{temporaryFileId}</c>, keeping any
    /// crops and focal point the snapshot has. The server fills in the file-derived values.
    /// </summary>
    /// <param name="body">The normalised snapshot body.</param>
    /// <param name="temporaryFileId">The staged file.</param>
    /// <returns>A new body.</returns>
    public static JsonNode WithStagedFile(JsonNode body, Guid temporaryFileId)
    {
        var clone = body.DeepClone();
        var file = FileObject(clone);
        file["temporaryFileId"] = temporaryFileId.ToString();
        return clone;
    }

    /// <summary>
    /// A write body that keeps the live item's file: <paramref name="body"/> (normalised) with the
    /// live <c>src</c> put back into <c>umbracoFile</c> and the live file-derived values restored.
    /// A PUT replaces every value, so leaving them out would drop the file.
    /// </summary>
    /// <param name="body">The normalised snapshot body.</param>
    /// <param name="live">The live item's verbatim body.</param>
    /// <returns>A new body.</returns>
    public static JsonNode WithLiveFile(JsonNode body, JsonNode live)
    {
        var clone = body.DeepClone();
        if (SrcOf(live) is { } src)
            FileObject(clone)["src"] = src;

        if (clone["values"] is JsonArray values)
            foreach (
                var value in (live["values"] as JsonArray ?? []).Where(v =>
                    AutoFilled.Contains(SnapshotBody.Text(v, "alias"))
                )
            )
                values.Add(value?.DeepClone());
        return clone;
    }

    /// <summary>The <c>umbracoFile</c> entry of a body's values, or null.</summary>
    /// <param name="body">A media body.</param>
    /// <returns>The value entry.</returns>
    private static JsonNode? FileValueEntry(JsonNode? body) =>
        (body?["values"] as JsonArray ?? []).FirstOrDefault(v =>
            SnapshotBody.Text(v, "alias") == FileAlias
        );

    /// <summary>
    /// The object inside a body's <c>umbracoFile</c> value, creating the entry (and the values
    /// array) when the body has none, so a file can always be set.
    /// </summary>
    /// <param name="body">The body to change.</param>
    /// <returns>The file value object.</returns>
    private static JsonObject FileObject(JsonNode body)
    {
        if (body["values"] is not JsonArray values)
            body["values"] = values = [];
        var entry = FileValueEntry(body);
        if (entry is null)
        {
            entry = new JsonObject { ["alias"] = FileAlias };
            values.Add(entry);
        }
        if (entry["value"] is not JsonObject file)
            entry["value"] = file = new JsonObject();
        return file;
    }
}
