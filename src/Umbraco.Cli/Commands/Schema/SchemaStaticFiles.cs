using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Partial views, stylesheets and scripts in the schema snapshot (#292). Templates travel in the
/// snapshot, and without the partials they render a promoted site answers every page with a 500,
/// so the three static-file kinds travel with them.
/// <para>
/// An entry is <c>{ "path": "/a/b.cshtml", "content": "..." }</c> for a file and
/// <c>{ "path": "/a", "isFolder": true }</c> for a folder, matched by path in Umbraco's leading-slash
/// form (the files have no ids). The helpers here are the pure parts: the entry shape, path rules,
/// the line-endings comparison, the export walk, and the template search behind the prune guard.
/// </para>
/// </summary>
public static class SchemaStaticFiles
{
    /// <summary>
    /// File content reads in flight at once (#422): 8, as for the content export's document reads.
    /// </summary>
    private const int FileReadConcurrency = 8;

    /// <summary>
    /// A path in Umbraco's form: one leading <c>/</c>, no trailing one (<c>blocklist/x/</c> becomes
    /// <c>/blocklist/x</c>), so a hand-edited snapshot matches the live tree.
    /// </summary>
    /// <param name="path">The path as written.</param>
    /// <returns>The normalised path.</returns>
    public static string NormalisePath(string path) => "/" + path.Trim().Trim('/');

    /// <summary>A file entry.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="content">The file content, byte for byte.</param>
    /// <returns>The entry.</returns>
    public static JsonObject File(string path, string content) =>
        new() { ["path"] = NormalisePath(path), ["content"] = content };

    /// <summary>A folder entry.</summary>
    /// <param name="path">The folder path.</param>
    /// <returns>The entry.</returns>
    public static JsonObject Folder(string path) =>
        new() { ["path"] = NormalisePath(path), ["isFolder"] = true };

    /// <summary>An entry's path, or an empty string when it has none.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The path.</returns>
    public static string PathOf(JsonNode? entry) =>
        entry?["path"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>Whether an entry is a folder.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>True for a folder.</returns>
    public static bool IsFolder(JsonNode? entry) =>
        entry?["isFolder"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>A file entry's content, or an empty string for a folder or a missing value.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The content.</returns>
    public static string ContentOf(JsonNode? entry) =>
        entry?["content"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>How deep a path is: <c>/a</c> is 1, <c>/a/b</c> is 2.</summary>
    /// <param name="path">A normalised path.</param>
    /// <returns>The number of segments.</returns>
    public static int Depth(string path) => path.Count(c => c == '/');

    /// <summary>The parent folder of a path, without slashes (<c>a/b</c>), or null at the root.</summary>
    /// <param name="path">A normalised path.</param>
    /// <returns>The parent path, or null.</returns>
    public static string? ParentOf(string path)
    {
        var cut = path.LastIndexOf('/');
        return cut <= 0 ? null : path[1..cut];
    }

    /// <summary>The last segment of a path: the file or folder name.</summary>
    /// <param name="path">A normalised path.</param>
    /// <returns>The name.</returns>
    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The entries with their paths normalised and a folder entry added for every folder a file
    /// or folder sits in that the list does not name. Apply then creates the folders a file needs
    /// even when a hand-written snapshot leaves them out, and a live folder that still holds a
    /// kept entry is never a prune candidate. The input is not changed.
    /// </summary>
    /// <param name="entries">The snapshot entries.</param>
    /// <returns>The completed entries.</returns>
    public static List<JsonNode> WithImpliedFolders(IEnumerable<JsonNode> entries)
    {
        var result = new List<JsonNode>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var clone = entry.DeepClone();
            clone["path"] = NormalisePath(PathOf(entry));
            if (paths.Add(PathOf(clone)))
                result.Add(clone);
        }
        foreach (var path in paths.ToList())
            for (var parent = ParentOf(path); parent is not null; parent = ParentOf("/" + parent))
                if (paths.Add("/" + parent))
                    result.Add(Folder(parent));
        return result;
    }

    /// <summary>
    /// Whether two contents differ only in line endings (CRLF, CR or LF) or in trailing newlines.
    /// Such a change is still reported and still applied (apply writes the snapshot's bytes), but
    /// the diff notes it, so a checkout's line-ending setting is not mistaken for an edit.
    /// </summary>
    /// <param name="a">One content.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they are equal once line endings and trailing newlines are ignored.</returns>
    public static bool DifferOnlyInLineEndings(string a, string b) =>
        a != b && Canonical(a) == Canonical(b);

    /// <summary>The content with every line break as LF and no trailing line breaks.</summary>
    /// <param name="content">The content.</param>
    /// <returns>The canonical form.</returns>
    private static string Canonical(string content) =>
        content.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');

    /// <summary>
    /// The terms that name a file inside a template, for the prune guard: the file name
    /// (<c>header.cshtml</c>, <c>site.css</c>), and for a partial view also its path without the
    /// extension in quotes, the form <c>Html.PartialAsync("header")</c> and
    /// <c>Html.PartialAsync("blocklist/default")</c> use. The extensionless form is quoted because
    /// a bare <c>header</c> would match most templates.
    /// </summary>
    /// <param name="kind">The static-file kind.</param>
    /// <param name="path">The file path.</param>
    /// <returns>The terms, matched ignoring case.</returns>
    public static IReadOnlyList<string> SearchTerms(StaticFileKind kind, string path)
    {
        var terms = new List<string> { NameOf(path) };
        if (kind == StaticFileKind.PartialView)
        {
            var bare = path.TrimStart('/');
            var dot = bare.LastIndexOf('.');
            if (dot > 0)
                bare = bare[..dot];
            terms.Add($"\"{bare}\"");
            terms.Add($"'{bare}'");
        }
        return terms;
    }

    /// <summary>Whether a template's content names the file, by any of its <see cref="SearchTerms"/>.</summary>
    /// <param name="templateContent">The template's Razor.</param>
    /// <param name="kind">The static-file kind.</param>
    /// <param name="path">The file path.</param>
    /// <returns>True when the template mentions it.</returns>
    public static bool Mentions(string templateContent, StaticFileKind kind, string path) =>
        SearchTerms(kind, path)
            .Any(t => templateContent.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads every file and folder of one kind into snapshot entries, walking the tree from the
    /// root and reading each file's content, a few at a time. Fails fast like the rest of the export: a partial
    /// list would diff as deletions. Entries are ordered by path so output is stable.
    /// </summary>
    /// <param name="client">The management client.</param>
    /// <param name="kind">The static-file kind.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entries, or the first failure.</returns>
    public static async Task<UmbracoResponse<List<JsonNode>>> CollectAsync(
        IStaticFileClient client,
        StaticFileKind kind,
        CancellationToken ct
    )
    {
        const int page = 100;
        var entries = new List<JsonNode>();
        var folders = new Queue<string?>([null]);
        while (folders.TryDequeue(out var parent))
        {
            for (var skip = 0; ; skip += page)
            {
                var listed = await client.GetStaticFilesAsync(kind, parent, skip, page, ct);
                if (!listed.IsSuccess)
                    return UmbracoResponse<List<JsonNode>>.FailureFrom(listed);
                var items = listed.Data!.Items.ToList();
                foreach (var folder in items.Where(i => i.IsFolder))
                {
                    entries.Add(Folder(NormalisePath(folder.Path)));
                    folders.Enqueue(folder.Path);
                }

                // The page's files are read a few at a time (#422). The walk itself stays one
                // request at a time, and the page's reads finish before the next page is listed,
                // so a failure is still the first one in walk order.
                var files = await ConcurrentReads.ReadAllAsync(
                    [.. items.Where(i => !i.IsFolder)],
                    FileReadConcurrency,
                    async (item, c) =>
                    {
                        var file = await client.GetStaticFileAsync(kind, item.Path, c);
                        return file.IsSuccess
                            ? UmbracoResponse<JsonNode>.Success(
                                File(NormalisePath(item.Path), file.Data!.Content ?? "")
                            )
                            : UmbracoResponse<JsonNode>.FailureFrom(file);
                    },
                    ct
                );
                if (!files.IsSuccess)
                    return UmbracoResponse<List<JsonNode>>.FailureFrom(files);
                entries.AddRange(files.Data!);
                if (items.Count == 0 || skip + items.Count >= listed.Data.Total)
                    break;
            }
        }
        return UmbracoResponse<List<JsonNode>>.Success([
            .. entries.OrderBy(PathOf, StringComparer.Ordinal),
        ]);
    }
}
