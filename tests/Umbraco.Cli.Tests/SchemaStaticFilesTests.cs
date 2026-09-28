using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Partial views, stylesheets and scripts in the schema snapshot (#292): the snapshot format,
/// "absent section means not managed", the path diff with its line-endings note, the apply order
/// (folders shallowest first, files before templates), the prune order, and the prune guard that
/// refuses a file a template names.
/// </summary>
public class SchemaStaticFilesTests
{
    private const string Partial = SchemaKinds.PartialView;

    /// <summary>A snapshot with only a partial-view section (null leaves the section out).</summary>
    private static SchemaSnapshot Snapshot(params JsonNode[]? partials) =>
        new() { PartialViews = partials is null ? null : [.. partials] };

    /// <summary>The live side: the given partial views.</summary>
    private static SchemaSnapshot Live(params JsonNode[] partials) =>
        new() { PartialViews = [.. partials] };

    // ── the snapshot file ────────────────────────────────────────────────────

    [Fact]
    public void FromJson_Version3_ReadsWithNoFileSections()
    {
        var snapshot = SchemaSnapshot.FromJson("""{ "schemaVersion": "3", "templates": [] }""");

        Assert.False(snapshot.ManagesFiles);
    }

    [Fact]
    public void FromJson_EmptySection_ManagesThatKind()
    {
        var snapshot = SchemaSnapshot.FromJson("""{ "schemaVersion": "4", "partialViews": [] }""");

        Assert.Equal(
            (true, 0, true),
            (snapshot.ManagesFiles, snapshot.PartialViews!.Count, snapshot.Scripts is null)
        );
    }

    [Fact]
    public void FromJson_UnknownVersion_IsRefused()
    {
        var error = Assert.Throws<JsonException>(() =>
            SchemaSnapshot.FromJson("""{ "schemaVersion": "5", "templates": [] }""")
        );

        Assert.Contains("'5'", error.Message);
    }

    [Fact]
    public void ToJson_NoFileSections_LeavesThemOut()
    {
        var json = new SchemaSnapshot().ToJson();

        Assert.DoesNotContain("partialViews", json);
    }

    // ── diff ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_Version3Snapshot_SkipsFilesEvenWhenLiveHasThem()
    {
        // A "3" file, or --no-files: not managed, so nothing to add, change or prune.
        var desired = SchemaSnapshot.FromJson("""{ "schemaVersion": "3", "templates": [] }""");

        var diff = SchemaDiffEngine.Compare(
            desired,
            Live(SchemaStaticFiles.File("/header.cshtml", "x"))
        );

        Assert.Same(SchemaKindDiff.None, diff.PartialViews);
    }

    [Fact]
    public void Compare_EmptySection_MakesEveryLiveFileAPruneCandidate()
    {
        var diff = SchemaDiffEngine.Compare(
            Snapshot(),
            Live(SchemaStaticFiles.File("/header.cshtml", "x"))
        );

        Assert.Equal("/header.cshtml", Assert.Single(diff.PartialViews.Removed).Identity);
    }

    [Fact]
    public void Compare_MatchesByPath_IgnoringSlashes()
    {
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.File("blocklist/default.cshtml/", "x")),
            Live(
                SchemaStaticFiles.Folder("/blocklist"),
                SchemaStaticFiles.File("/blocklist/default.cshtml", "x")
            )
        );

        Assert.Equal((false, 2), (diff.PartialViews.HasChanges, diff.PartialViews.Unchanged));
    }

    [Fact]
    public void Compare_ContentDiffers_IsChanged()
    {
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.File("/header.cshtml", "<h1>New</h1>")),
            Live(SchemaStaticFiles.File("/header.cshtml", "<h1>Old</h1>"))
        );

        var change = Assert.Single(diff.PartialViews.Changed);
        Assert.Equal(("content", (string?)null), (string.Join(",", change.Changes!), change.Note));
    }

    [Fact]
    public void Compare_OnlyLineEndingsDiffer_IsChangedWithANote()
    {
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.File("/header.cshtml", "a\r\nb\r\n")),
            Live(SchemaStaticFiles.File("/header.cshtml", "a\nb"))
        );

        Assert.Equal("line endings only", Assert.Single(diff.PartialViews.Changed).Note);
    }

    [Fact]
    public void Compare_FileBecomesFolder_IsSkipped()
    {
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.Folder("/header")),
            Live(SchemaStaticFiles.File("/header", "x"))
        );

        Assert.Equal((0, 1), (diff.PartialViews.Changed.Count, diff.PartialViews.Skipped.Count));
    }

    [Fact]
    public void Compare_LiveFolderHoldingAKeptFile_IsNotPruned()
    {
        // The snapshot names the file but not its folder: the folder is implied, so kept.
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.File("/blocklist/default.cshtml", "x")),
            Live(
                SchemaStaticFiles.Folder("/blocklist"),
                SchemaStaticFiles.File("/blocklist/default.cshtml", "x")
            )
        );

        Assert.Empty(diff.PartialViews.Removed);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a\r\nb", "a\nb", true)]
    [InlineData("a\nb\n", "a\nb", true)]
    [InlineData("a\nb", "a\nb", false)]
    [InlineData("a\nb", "a\nc", false)]
    public void DifferOnlyInLineEndings_Cases(string a, string b, bool expected)
    {
        Assert.Equal(expected, SchemaStaticFiles.DifferOnlyInLineEndings(a, b));
    }

    [Theory]
    [InlineData("@await Html.PartialAsync(\"header\")", "/header.cshtml", true)]
    [InlineData(
        "@await Html.PartialAsync(\"blocklist/default\")",
        "/blocklist/default.cshtml",
        true
    )]
    [InlineData(
        "@await Html.PartialAsync(\"~/Views/Partials/header.cshtml\")",
        "/header.cshtml",
        true
    )]
    [InlineData("@await Html.PartialAsync(\"headerNav\")", "/header.cshtml", false)]
    public void Mentions_PartialView(string template, string path, bool expected)
    {
        Assert.Equal(
            expected,
            SchemaStaticFiles.Mentions(template, StaticFileKind.PartialView, path)
        );
    }

    [Fact]
    public void Mentions_Stylesheet_NeedsTheFileName()
    {
        // A stylesheet's bare name ("site") would match most templates, so only "site.css" counts.
        Assert.Equal(
            (true, false),
            (
                SchemaStaticFiles.Mentions(
                    "<link href=\"/css/site.css\" />",
                    StaticFileKind.Stylesheet,
                    "/site.css"
                ),
                SchemaStaticFiles.Mentions("\"site\"", StaticFileKind.Stylesheet, "/site.css")
            )
        );
    }

    [Fact]
    public void WithImpliedFolders_AddsEveryAncestor()
    {
        var entries = SchemaStaticFiles.WithImpliedFolders([
            SchemaStaticFiles.File("a/b/c.cshtml", "x"),
        ]);

        Assert.Equal(
            ["/a", "/a/b", "/a/b/c.cshtml"],
            entries.Select(SchemaStaticFiles.PathOf).Order(StringComparer.Ordinal)
        );
    }

    // ── export ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_WalksNestedFolders_InPathOrder()
    {
        var fake = new FakeUmbracoManagementClient();
        var tree = fake.StaticFileTree[StaticFileKind.PartialView];
        tree["/header.cshtml"] = "<h1/>";
        tree["/blocklist"] = null;
        tree["/blocklist/Components"] = null;
        tree["/blocklist/Components/title.cshtml"] = "t";

        var result = await SchemaStaticFiles.CollectAsync(
            fake,
            StaticFileKind.PartialView,
            CancellationToken.None
        );

        Assert.Equal(
            [
                "/blocklist",
                "/blocklist/Components",
                "/blocklist/Components/title.cshtml",
                "/header.cshtml",
            ],
            result.Data!.Select(SchemaStaticFiles.PathOf)
        );
    }

    [Fact]
    public async Task CollectAsync_KeepsContentByteForByte()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.StaticFileTree[StaticFileKind.Script]["/site.js"] = "a\r\nb\r\n";

        var result = await SchemaStaticFiles.CollectAsync(
            fake,
            StaticFileKind.Script,
            CancellationToken.None
        );

        Assert.Equal("a\r\nb\r\n", SchemaStaticFiles.ContentOf(Assert.Single(result.Data!)));
    }

    [Fact]
    public async Task CollectAsync_ListFails_ReturnsTheFailure()
    {
        var fake = new FakeUmbracoManagementClient
        {
            StaticFileListFailure = UmbracoResponse<PagedResponse<StaticFileTreeItem>>.Failure(
                500,
                "boom"
            ),
        };

        var result = await SchemaStaticFiles.CollectAsync(
            fake,
            StaticFileKind.Script,
            CancellationToken.None
        );

        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public async Task ExportAsync_WithoutFiles_LeavesTheSectionsOut()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.StaticFileTree[StaticFileKind.Script]["/site.js"] = "x";

        var result = await SchemaExporter.ExportAsync(
            fake,
            CancellationToken.None,
            includeFiles: false
        );

        Assert.False(result.Data!.ManagesFiles);
    }

    [Fact]
    public async Task ExportAsync_Default_CarriesEveryFileKind()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.StaticFileTree[StaticFileKind.Script]["/site.js"] = "x";

        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        Assert.Equal(
            (0, 0, 1),
            (
                result.Data!.PartialViews!.Count,
                result.Data.Stylesheets!.Count,
                result.Data.Scripts!.Count
            )
        );
    }

    // ── apply ────────────────────────────────────────────────────────────────

    /// <summary>A diff with the given partial-view diff and an added template.</summary>
    private static SchemaDiff DiffWith(SchemaKindDiff partials, SchemaKindDiff? templates = null) =>
        new(
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            templates ?? SchemaKindDiff.None
        )
        {
            PartialViews = partials,
        };

    private static Task<UmbracoResponse<SchemaApplyResult>> Apply(
        FakeUmbracoManagementClient fake,
        SchemaDiff diff,
        bool prune = false,
        bool dryRun = false,
        bool force = false
    ) =>
        SchemaApplier.ApplyAsync(
            fake,
            diff,
            new SchemaApplyOptions(prune, dryRun, force),
            CancellationToken.None
        );

    [Fact]
    public async Task ApplyAsync_NestedFile_CreatesFoldersShallowestFirstThenTheFile()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.File("/blocklist/Components/title.cshtml", "t")),
            Live()
        );

        await Apply(fake, diff);

        Assert.Equal(
            [
                "create-folder PartialView /blocklist",
                "create-folder PartialView /blocklist/Components",
                "create PartialView /blocklist/Components/title.cshtml",
            ],
            fake.StaticFileWrites
        );
    }

    [Fact]
    public async Task ApplyAsync_WritesTheSnapshotContentByteForByte()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = SchemaDiffEngine.Compare(
            Snapshot(SchemaStaticFiles.File("/header.cshtml", "a\r\nb")),
            Live(SchemaStaticFiles.File("/header.cshtml", "a\nb"))
        );

        await Apply(fake, diff);

        Assert.Equal("a\r\nb", Assert.Single(fake.StaticFilesUpdated).Content);
    }

    [Fact]
    public async Task ApplyAsync_FilesComeBeforeTemplates()
    {
        var template = new SchemaEntityChange(
            SchemaKinds.Template,
            SchemaChangeKind.Added,
            "home",
            Guid.NewGuid(),
            null
        )
        {
            DesiredBody = new JsonObject { ["alias"] = "home" },
        };
        var diff = DiffWith(
            SchemaDiffEngine
                .Compare(Snapshot(SchemaStaticFiles.File("/header.cshtml", "x")), Live())
                .PartialViews,
            new SchemaKindDiff([template], [], [], [], 0)
        );

        var result = await Apply(new FakeUmbracoManagementClient(), diff, dryRun: true);

        Assert.Equal([Partial, SchemaKinds.Template], result.Data!.Actions.Select(a => a.Kind));
    }

    [Fact]
    public async Task ApplyAsync_Prune_DeletesFilesThenFoldersDeepestFirst()
    {
        var fake = new FakeUmbracoManagementClient();
        var diff = SchemaDiffEngine.Compare(
            Snapshot(),
            Live(
                SchemaStaticFiles.Folder("/a"),
                SchemaStaticFiles.Folder("/a/b"),
                SchemaStaticFiles.File("/a/b/c.cshtml", "x")
            )
        );

        await Apply(fake, diff, prune: true);

        Assert.Equal(
            [
                "delete PartialView /a/b/c.cshtml",
                "delete-folder PartialView /a/b",
                "delete-folder PartialView /a",
            ],
            fake.StaticFileWrites
        );
    }

    /// <summary>A fake whose one template's content is <paramref name="content"/>.</summary>
    private static FakeUmbracoManagementClient WithTemplate(string content)
    {
        var fake = new FakeUmbracoManagementClient();
        var id = Guid.NewGuid();
        fake.TemplateList.Add(new TemplateResponse { Id = id, Name = "Home" });
        fake.TemplateRaw[id] = new JsonObject
        {
            ["id"] = id.ToString(),
            ["name"] = "Home",
            ["alias"] = "home",
            ["content"] = content,
        };
        return fake;
    }

    private static SchemaDiff PruneHeader() =>
        SchemaDiffEngine.Compare(Snapshot(), Live(SchemaStaticFiles.File("/header.cshtml", "x")));

    [Fact]
    public async Task ApplyAsync_PruneFileATemplateNames_IsRefusedNamingTheTemplate()
    {
        var fake = WithTemplate("@await Html.PartialAsync(\"header\")");

        var error = await Assert.ThrowsAsync<SafetyRefusalException>(() =>
            Apply(fake, PruneHeader(), prune: true)
        );

        Assert.Equal((true, 0), (error.Message.Contains("'Home'"), fake.StaticFilesDeleted.Count));
    }

    [Fact]
    public async Task ApplyAsync_PruneFileATemplateNames_WithForce_Deletes()
    {
        var fake = WithTemplate("@await Html.PartialAsync(\"header\")");

        await Apply(fake, PruneHeader(), prune: true, force: true);

        Assert.Single(fake.StaticFilesDeleted);
    }

    [Fact]
    public async Task ApplyAsync_PruneFileNoTemplateNames_Deletes()
    {
        var fake = WithTemplate("<p>no partials here</p>");

        await Apply(fake, PruneHeader(), prune: true);

        Assert.Equal("/header.cshtml", Assert.Single(fake.StaticFilesDeleted).Path);
    }

    [Fact]
    public async Task ApplyAsync_DryRun_MarksAFileATemplateNames()
    {
        var fake = WithTemplate("@await Html.PartialAsync(\"header\")");

        var result = await Apply(fake, PruneHeader(), prune: true, dryRun: true);

        Assert.Equal("needs --force", Assert.Single(result.Data!.Actions).Status);
    }

    [Fact]
    public void FileReason_TemplatesUnreadable_AsksForForce()
    {
        var reason = SchemaApplier.FileReason(StaticFileKind.PartialView, "/header.cshtml", null);

        Assert.Contains("Could not read the templates", reason);
    }
}
