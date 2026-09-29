using System.Net;
using System.Text.Json.Nodes;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How many requests <c>content export</c>, <c>schema export</c> and <c>schema diff</c> make
/// (#408). An export enumerates each kind and then reads every entity's full body, because the
/// trees do not carry it, so the count grows with the size of the instance by design: the pages
/// that enumerate each kind, then one read per entity, or one batch per 40 for the four type kinds
/// (#418). The user- and member-group lists carry the whole body, so they need no more (#413).
/// </summary>
[Collection("ConsoleCapture")]
public sealed class ExportRequestCountTests : IDisposable
{
    /// <summary>How many entities <see cref="SchemaSite"/> holds of each schema kind.</summary>
    private const int PerKind = 2;

    private readonly string _dir = Directory.CreateTempSubdirectory("umbraco-rc-").FullName;

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ContentExport_SixDocumentsTwoWithChildren_WalksTheTreeAndReadsEachDocument()
    {
        // Arrange: three at the root, two under the first, one under the first of those.
        var type = Guid.NewGuid();
        var documents = new FakeTree();
        var roots = documents.AddMany(3, null, i => FakeUmbraco.DocumentItem($"Root {i}", type));
        var children = documents.AddMany(
            2,
            roots[0],
            i => FakeUmbraco.DocumentItem($"Child {i}", type)
        );
        documents.AddMany(1, children[0], i => FakeUmbraco.DocumentItem($"Leaf {i}", type));
        var cli = new HttpCli(FakeUmbraco.ContentSite(documents));

        // Act
        var run = await cli.RunAsync("content export");

        // Assert
        run.HasRequestCount(
            (1 + 2) + 6,
            "1 root level + 1 level per node with children, + 1 body read per document"
        );
    }

    [Fact]
    public async Task SchemaExport_TwoOfEachKind_ReadsEachKindsListThenTheBodies()
    {
        // Arrange
        var cli = new HttpCli(SchemaSite());

        // Act
        var run = await cli.RunAsync("schema export");

        // Assert: twelve kinds - five type trees, three file trees, languages, dictionary, member
        // and user groups - each listed in one page; languages and groups come whole with their
        // list.
        run.HasRequestCount(
            12 + 4 + 5 * PerKind,
            $"1 list page per kind (12) + 1 batch per type kind (4) + 1 read per template, "
                + $"dictionary item and file ({PerKind} of each of 5 kinds)"
        );
    }

    [Fact]
    public async Task SchemaExportNoFiles_TwoOfEachKind_LeavesTheFileKindsUnread()
    {
        // Arrange
        var cli = new HttpCli(SchemaSite());

        // Act
        var run = await cli.RunAsync("schema export --no-files");

        // Assert
        run.HasRequestCount(
            9 + 4 + 2 * PerKind,
            $"1 list page per non-file kind (9) + 1 batch per type kind (4) + 1 read per template "
                + $"and dictionary item ({PerKind} each)"
        );
    }

    [Fact]
    public async Task SchemaExport_UserAndMemberGroups_BuildsEachGroupFromItsList()
    {
        // Arrange
        var cli = new HttpCli(SchemaSite());

        // Act
        var run = await cli.RunAsync("schema export");

        // Assert: the paged lists return each group's full body, the same model the by-id read
        // returns, so no group is read again (#413).
        run.HasRequestCount(
            IsGroupRequest,
            "user-group and member-group",
            2,
            $"1 list page per group kind, and no by-id read ({PerKind} groups of each)"
        );
    }

    [Fact]
    public async Task SchemaDiff_UnchangedExport_ReadsWhatTheExportRead()
    {
        // Arrange
        var cli = new HttpCli(SchemaSite());
        var snapshot = Path.Combine(_dir, "schema.json");
        await cli.RunAsync($"schema export --out {snapshot}");

        // Act
        var run = await cli.RunAsync($"schema diff {snapshot}");

        // Assert
        run.HasRequestCount(
            12 + 4 + 5 * PerKind,
            "the same reads as schema export: every kind the snapshot manages"
        );
    }

    [Fact]
    public async Task SchemaDiff_SnapshotOfDocumentTypesOnly_ReadsOnlyDocumentTypes()
    {
        // Arrange: a hand-written snapshot manages only the sections it has (#198, #292).
        var cli = new HttpCli(SchemaSite());
        var snapshot = Path.Combine(_dir, "document-types.json");
        await File.WriteAllTextAsync(snapshot, """{"schemaVersion":"4","documentTypes":[]}""");

        // Act
        var run = await cli.RunAsync($"schema diff {snapshot}");

        // Assert
        run.HasRequestCount(
            1 + 1,
            $"1 document-type tree page + 1 batch for the {PerKind} document types"
        );
    }

    /// <summary>Whether a request is to the user-group or member-group endpoints.</summary>
    /// <param name="request">The recorded request.</param>
    /// <returns>True for a group list or by-id read.</returns>
    private static bool IsGroupRequest(Recorded request) =>
        request.Uri.AbsolutePath.Contains("/user-group")
        || request.Uri.AbsolutePath.Contains("/member-group");

    /// <summary>The kinds read by walking a type tree, then by id.</summary>
    private static readonly string[] TreeKinds =
    [
        "document-type",
        "media-type",
        "member-type",
        "data-type",
        "template",
    ];

    /// <summary>The static-file kinds and their file extensions, read by walking a tree, then by path.</summary>
    private static readonly (string Kind, string Extension)[] FileKinds =
    [
        ("partial-view", "cshtml"),
        ("stylesheet", "css"),
        ("script", "js"),
    ];

    /// <summary>The kinds read from a paged collection, then by id.</summary>
    private static readonly string[] CollectionKinds = ["dictionary", "member-group", "user-group"];

    /// <summary>
    /// An instance with <see cref="PerKind"/> entities of every schema kind, all at the root, and
    /// two languages.
    /// </summary>
    /// <returns>The handler.</returns>
    private static RoutingHandler SchemaSite()
    {
        var handler = new RoutingHandler();
        foreach (var kind in TreeKinds)
        {
            var tree = new FakeTree();
            tree.AddMany(PerKind, null, i => FakeUmbraco.NamedItem($"{kind} {i}"));
            handler.ServeTree(kind, tree).ServeType(kind, FakeUmbraco.ContentType);
        }

        foreach (var (kind, extension) in FileKinds)
        {
            var files = new FakeTree();
            files.AddMany(PerKind, null, i => FileItem($"/f{i}.{extension}"));
            handler
                .ServeTree(kind, files)
                .When(
                    r =>
                        r.Method == HttpMethod.Get
                        && r.RequestUri!.AbsolutePath.Contains($"/v1/{kind}/"),
                    HttpStatusCode.OK,
                    $$"""{"name":"f.{{extension}}","path":"/f.{{extension}}","content":"x"}"""
                );
        }

        foreach (var kind in CollectionKinds)
        {
            var items = Enumerable
                .Range(0, PerKind)
                .Select(i => FakeUmbraco.NamedItem($"{kind} {i}"));
            handler
                .ServeCollection($"/{kind}", [.. items])
                .ServeById(kind, FakeUmbraco.ContentType);
        }

        return handler
            .ServeCollection(
                "/language",
                [Language("en-US", isDefault: true), Language("da-DK", isDefault: false)]
            )
            .ElseEmpty();
    }

    /// <summary>A static-file tree item: a file, not a folder, at <paramref name="path"/>.</summary>
    /// <param name="path">The file path, e.g. <c>/f0.css</c>.</param>
    /// <returns>The tree-item fields.</returns>
    private static JsonObject FileItem(string path) =>
        new()
        {
            ["name"] = path.TrimStart('/'),
            ["path"] = path,
            ["isFolder"] = false,
        };

    /// <summary>A language as the paged <c>/language</c> list returns it.</summary>
    /// <param name="isoCode">The ISO code.</param>
    /// <param name="isDefault">Whether it is the default language.</param>
    /// <returns>The item.</returns>
    private static JsonObject Language(string isoCode, bool isDefault) =>
        new()
        {
            ["isoCode"] = isoCode,
            ["name"] = isoCode,
            ["isDefault"] = isDefault,
        };
}
