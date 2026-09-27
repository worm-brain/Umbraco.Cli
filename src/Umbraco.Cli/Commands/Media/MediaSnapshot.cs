using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Umbraco.Cli.Commands.Media;

/// <summary>
/// The file a media item holds, as stored in the snapshot directory (#226). The size and hash let
/// diff tell whether the target holds the same file without downloading it.
/// </summary>
public sealed class MediaFile
{
    /// <summary>The file's path inside the snapshot directory, with forward slashes (<c>files/{id}/{name}</c>).</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>The file name the item stores it under (the last segment of its <c>src</c>).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>The file size in bytes.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }

    /// <summary>The file's SHA-256, lower-case hex.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
}

/// <summary>
/// One media item in a <see cref="MediaSnapshot"/>: its verbatim <c>GET /media/{id}</c> body, its
/// tree placement (the body has none), and its file, if it holds one (folders do not).
/// </summary>
public sealed class MediaNode
{
    /// <summary>The item's GUID, kept on every instance (apply recreates with it).</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>The parent item's id, or null at the media root.</summary>
    [JsonPropertyName("parent")]
    public Guid? Parent { get; init; }

    /// <summary>The verbatim <c>GET /media/{id}</c> body.</summary>
    [JsonPropertyName("body")]
    public JsonNode Body { get; init; } = new JsonObject();

    /// <summary>The item's file, or null when it holds none.</summary>
    [JsonPropertyName("file")]
    public MediaFile? File { get; init; }
}

/// <summary>
/// A portable dump of a media subtree (#226, ADR 0008): every item's verbatim body and placement
/// in tree pre-order, with its file stored beside the index. Unlike the schema and content
/// snapshots it is a <b>directory</b> - <see cref="IndexFileName"/> plus <c>files/{id}/{name}</c> -
/// because the files are binary. Produced by <c>media export</c>, consumed by <c>media diff</c> and
/// <c>media apply</c>.
/// </summary>
public sealed class MediaSnapshot
{
    /// <summary>The name of the index file inside a snapshot directory.</summary>
    public const string IndexFileName = "media.json";

    /// <summary>The name of the directory the files are stored in.</summary>
    public const string FilesDirectoryName = "files";

    /// <summary>The current snapshot layout version emitted by <c>media export</c>.</summary>
    public const string CurrentVersion = "1";

    /// <summary>The snapshot layout version, so a future change to the shape is detected.</summary>
    [JsonPropertyName("mediaVersion")]
    public string MediaVersion { get; init; } = CurrentVersion;

    /// <summary>
    /// The subtree root this snapshot was scoped to, or null for the whole media tree. Diff and
    /// apply read the live tree at the same root, so a subtree's <c>--prune</c> never reaches
    /// outside it (ADR 0006 §3).
    /// </summary>
    [JsonPropertyName("root")]
    public Guid? Root { get; init; }

    /// <summary>The items, in tree pre-order (parents before children).</summary>
    [JsonPropertyName("items")]
    public List<MediaNode> Items { get; init; } = [];

    /// <summary>The directory the snapshot was loaded from; files resolve against it. Not serialized.</summary>
    [JsonIgnore]
    public string Directory { get; init; } = "";

    /// <summary>Serializer settings shared by export, diff and apply (indented, camelCase, nulls omitted).</summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes the index to indented JSON.</summary>
    /// <returns>The index as JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>The absolute path of an item's stored file.</summary>
    /// <param name="file">The file entry.</param>
    /// <returns>The path on disk.</returns>
    public string PathOf(MediaFile file) =>
        System.IO.Path.Combine(
            Directory,
            file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)
        );

    /// <summary>
    /// Parses a snapshot index. As with the other snapshots this is the trust boundary for
    /// <c>apply --prune</c>: text that is not recognisably a media snapshot (an <c>{}</c>, say) is
    /// refused rather than read as "the target should have no media".
    /// </summary>
    /// <param name="json">The index JSON.</param>
    /// <param name="directory">The snapshot directory, for resolving files.</param>
    /// <returns>The parsed snapshot.</returns>
    /// <exception cref="JsonException">The text is not a media snapshot, or is an unsupported version.</exception>
    public static MediaSnapshot FromJson(string json, string directory)
    {
        if (
            JsonNode.Parse(json) is not JsonObject obj
            || !(obj.ContainsKey("mediaVersion") || obj.ContainsKey("items"))
        )
            throw new JsonException(
                $"Not a media snapshot: expected a 'mediaVersion' and/or an 'items' array "
                    + "(produced by 'media export')."
            );

        if (
            obj["mediaVersion"] is JsonValue versionNode
            && versionNode.TryGetValue<string>(out var version)
            && version != CurrentVersion
        )
            throw new JsonException(
                $"Unsupported snapshot mediaVersion '{version}' (this CLI writes and reads "
                    + $"'{CurrentVersion}'). Re-export with a matching CLI version."
            );

        var snapshot =
            obj.Deserialize<MediaSnapshot>(SerializerOptions)
            ?? throw new JsonException("The snapshot could not be parsed.");

        // A snapshot can come from someone else, and apply uploads the files it names: a path
        // that leaves files/ ("../../.ssh/id_rsa") would send an arbitrary local file to the
        // instance. Only paths inside the snapshot's own files directory are accepted.
        var filesRoot =
            System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, FilesDirectoryName))
            + System.IO.Path.DirectorySeparatorChar;
        foreach (var file in snapshot.Items.Select(i => i.File).OfType<MediaFile>())
        {
            var resolved = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    directory,
                    file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)
                )
            );
            if (!resolved.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase))
                throw new JsonException(
                    $"The snapshot names the file '{file.Path}', which is outside its "
                        + $"{FilesDirectoryName} directory."
                );
        }
        return new MediaSnapshot
        {
            Root = snapshot.Root,
            Items = snapshot.Items,
            Directory = directory,
        };
    }

    /// <summary>
    /// Loads a snapshot from its directory, or from the path of its index file. Stdin is refused:
    /// the files cannot come with it.
    /// </summary>
    /// <param name="path">The snapshot directory, or the path of its <see cref="IndexFileName"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="InvalidInputException">The path is <c>-</c>.</exception>
    /// <exception cref="FileNotFoundException">There is no index at the path.</exception>
    /// <exception cref="JsonException">The index is not a media snapshot.</exception>
    public static async Task<MediaSnapshot> LoadAsync(string path, CancellationToken ct)
    {
        if (path == "-")
            throw new InvalidInputException(
                "A media snapshot is a directory (an index and its files), so it cannot be read "
                    + "from stdin. Pass the directory 'media export --out' wrote."
            );

        var index = System.IO.Directory.Exists(path)
            ? System.IO.Path.Combine(path, IndexFileName)
            : path;
        if (!File.Exists(index))
            throw new FileNotFoundException(
                $"No media snapshot at '{path}': expected a directory holding {IndexFileName}.",
                index
            );

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(index))!;
        return FromJson(await File.ReadAllTextAsync(index, ct), directory);
    }
}
