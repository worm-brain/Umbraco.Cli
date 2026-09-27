using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.Media;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The media export/diff/apply pipeline (#226): GUID-preserving, with the files carried in the
/// snapshot directory. Covers the body rules (what differs per instance), the directory snapshot,
/// export, the file comparison, and apply's staging, file keeping and trash-based prune. Driven
/// through the fake client and a temporary directory per test.
/// </summary>
public sealed class MediaSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"media-{Guid.NewGuid()}");

    /// <summary>Removes the test's snapshot directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static readonly Guid ImageType = Guid.NewGuid();

    private static readonly byte[] Photo = Encoding.UTF8.GetBytes("jpeg bytes");

    /// <summary>An image item as <c>GET /media/{id}</c> returns it on one instance.</summary>
    private static JsonNode Image(
        Guid id,
        string src,
        long bytes,
        string alt = "A photo",
        string date = "2026-09-01T10:00:00Z"
    ) =>
        JsonNode.Parse(
            $$$"""
            {"id":"{{{id}}}","isTrashed":false,"flags":[],"mediaType":{"id":"{{{ImageType}}}"},
             "values":[
               {"alias":"umbracoFile","culture":null,"segment":null,"editorAlias":"Umbraco.ImageCropper",
                "value":{"src":"{{{src}}}","crops":[],"focalPoint":{"left":0.5,"top":0.5} } },
               {"alias":"umbracoBytes","culture":null,"segment":null,"value":"{{{bytes}}}"},
               {"alias":"umbracoExtension","culture":null,"segment":null,"value":"jpg"},
               {"alias":"altText","culture":null,"segment":null,"value":"{{{alt}}}"}],
             "variants":[{"culture":null,"segment":null,"name":"Photo","createDate":"{{{date}}}","updateDate":"{{{date}}}"}]}
            """
        )!;

    /// <summary>A folder item (no file).</summary>
    private static JsonNode Folder(Guid id) =>
        JsonNode.Parse(
            $$"""{"id":"{{id}}","mediaType":{"id":"{{Guid.NewGuid()}}"},"values":[],"variants":[{"culture":null,"name":"Blog"}]}"""
        )!;

    private static MediaFile FileOf(Guid id, string name, byte[] content) =>
        new()
        {
            Path = $"files/{id}/{name}",
            Name = name,
            Bytes = content.Length,
            Sha256 = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(content)
            ),
        };

    private static MediaNode Live(Guid id, Guid? parent, JsonNode body) =>
        new()
        {
            Id = id,
            Parent = parent,
            Body = body,
            File = MediaBody.SrcOf(body) is { } src
                ? new MediaFile
                {
                    Name = MediaBody.FileNameOf(src),
                    Bytes = MediaBody.BytesOf(body) ?? -1,
                }
                : null,
        };

    /// <summary>A snapshot directory holding one image item and its file.</summary>
    private MediaSnapshot SnapshotWithImage(Guid id, byte[] content, Guid? parent = null)
    {
        var path = Path.Combine(_dir, "files", id.ToString(), "photo.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return new MediaSnapshot
        {
            Directory = _dir,
            Items =
            [
                new MediaNode
                {
                    Id = id,
                    Parent = parent,
                    Body = Image(id, "/media/src1/photo.jpg", content.Length),
                    File = FileOf(id, "photo.jpg", content),
                },
            ],
        };
    }

    // ── body ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalise_SameFileOnTwoInstances_ComparesEqual()
    {
        // Arrange: another upload folder, other dates - what every instance differs in.
        var id = Guid.NewGuid();
        var here = Image(id, "/media/aaa/photo.jpg", 10);
        var there = Image(id, "/media/zzz/photo.jpg", 10, date: "2026-09-20T08:00:00Z");

        // Act
        var changes = JsonPathDiff.Paths(MediaBody.Normalise(here), MediaBody.Normalise(there));

        // Assert
        Assert.Empty(changes);
    }

    [Fact]
    public void Normalise_KeepsTheCropsAndFocalPoint()
    {
        var normalised = MediaBody.Normalise(Image(Guid.NewGuid(), "/media/a/p.jpg", 10));

        var file = normalised["values"]!
            .AsArray()
            .Single(v => (string?)v!["alias"] == "umbracoFile")!;
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse("""{"crops":[],"focalPoint":{"left":0.5,"top":0.5}}"""),
                file["value"]
            ),
            file.ToJsonString()
        );
    }

    [Fact]
    public void WithLiveFile_PutsBackTheLiveSrcAndSize()
    {
        var id = Guid.NewGuid();
        var live = Image(id, "/media/live/photo.jpg", 10);

        var body = MediaBody.WithLiveFile(MediaBody.Normalise(live), live);

        Assert.Multiple(
            () => Assert.Equal("/media/live/photo.jpg", MediaBody.SrcOf(body)),
            () => Assert.Equal(10, MediaBody.BytesOf(body))
        );
    }

    [Fact]
    public void FileNameOf_DecodesTheLastSegment()
    {
        Assert.Equal("my photo.jpg", MediaBody.FileNameOf("/media/abc/my%20photo.jpg?v=1"));
    }

    // ── snapshot ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_Stdin_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidInputException>(() =>
            MediaSnapshot.LoadAsync("-", CancellationToken.None)
        );
    }

    [Fact]
    public void FromJson_NotASnapshot_IsRefused()
    {
        // An empty object would otherwise read as "the target should have no media".
        Assert.Throws<JsonException>(() => MediaSnapshot.FromJson("{}", _dir));
    }

    [Fact]
    public void FromJson_UnknownVersion_IsRefused()
    {
        Assert.Throws<JsonException>(() =>
            MediaSnapshot.FromJson("""{"mediaVersion":"9","items":[]}""", _dir)
        );
    }

    // ── export ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_WritesTheIndexAndEachFileUnderItsId()
    {
        // Arrange: a folder holding one image.
        var folder = Guid.NewGuid();
        var image = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.MediaSnapshotTree.Add(new ContentTreeNode(folder, null));
        fake.MediaSnapshotTree.Add(new ContentTreeNode(image, folder));
        fake.MediaRaw[folder] = Folder(folder);
        fake.MediaRaw[image] = Image(image, "/media/abc/photo.jpg", Photo.Length);
        fake.MediaFiles["/media/abc/photo.jpg"] = Photo;

        // Act
        var result = await MediaExporter.ExportAsync(fake, null, _dir, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var loaded = await MediaSnapshot.LoadAsync(_dir, CancellationToken.None);
        var file = loaded.Items.Single(i => i.Id == image).File!;
        Assert.Multiple(
            () => Assert.Equal($"files/{image}/photo.jpg", file.Path),
            () => Assert.Equal(Photo, File.ReadAllBytes(loaded.PathOf(file))),
            () => Assert.Equal(Photo.Length, file.Bytes),
            () => Assert.Null(loaded.Items.Single(i => i.Id == folder).File),
            () => Assert.Equal(folder, loaded.Items.Single(i => i.Id == image).Parent)
        );
    }

    [Fact]
    public async Task ExportAsync_FailedDownload_FailsAndLeavesNoIndex()
    {
        var image = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.MediaSnapshotTree.Add(new ContentTreeNode(image, null));
        fake.MediaRaw[image] = Image(image, "/media/abc/missing.jpg", 10);

        var result = await MediaExporter.ExportAsync(fake, null, _dir, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.False(File.Exists(Path.Combine(_dir, MediaSnapshot.IndexFileName)))
        );
    }

    [Fact]
    public async Task ExportAsync_IntoAnUnrelatedNonEmptyDirectory_IsRefused()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "mine");

        await Assert.ThrowsAsync<InvalidInputException>(() =>
            MediaExporter.ExportAsync(
                new FakeUmbracoManagementClient(),
                null,
                _dir,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ExportAsync_OverAnEarlierExport_RemovesItsFiles()
    {
        // Arrange: an earlier export with a file the new one does not have.
        var stale = Path.Combine(_dir, "files", Guid.NewGuid().ToString(), "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "old");
        File.WriteAllText(Path.Combine(_dir, MediaSnapshot.IndexFileName), "{}");

        // Act
        await MediaExporter.ExportAsync(
            new FakeUmbracoManagementClient(),
            null,
            _dir,
            CancellationToken.None
        );

        // Assert
        Assert.False(File.Exists(stale));
    }

    // ── diff ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_SameFileAndBody_IsUnchanged()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);

        var diff = MediaDiffEngine.Compare(
            snapshot,
            [Live(id, null, Image(id, "/media/other/photo.jpg", Photo.Length))]
        );

        Assert.Multiple(() => Assert.Empty(diff.Items), () => Assert.Equal(1, diff.Unchanged));
    }

    [Fact]
    public void Compare_LiveFileOfAnotherSize_IsChangedWithFile()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);

        var diff = MediaDiffEngine.Compare(
            snapshot,
            [Live(id, null, Image(id, "/media/other/photo.jpg", Photo.Length + 1))]
        );

        var change = Assert.Single(diff.Items);
        Assert.Equal(["file"], change.Changes);
    }

    [Fact]
    public void Compare_LiveFileWithAnotherHash_IsChanged()
    {
        // Same name and size, but --verify-files hashed the live file and it differs.
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);
        var live = Live(id, null, Image(id, "/media/other/photo.jpg", Photo.Length));
        var hashed = new MediaNode
        {
            Id = id,
            Body = live.Body,
            File = new MediaFile
            {
                Name = "photo.jpg",
                Bytes = Photo.Length,
                Sha256 = "00",
            },
        };

        var diff = MediaDiffEngine.Compare(snapshot, [hashed]);

        Assert.True(Assert.Single(diff.Items).FileChanged);
    }

    [Fact]
    public void Compare_OnlyAltTextDiffers_IsChangedWithoutFile()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);

        var diff = MediaDiffEngine.Compare(
            snapshot,
            [Live(id, null, Image(id, "/media/other/photo.jpg", Photo.Length, alt: "Old"))]
        );

        var change = Assert.Single(diff.Items);
        Assert.Multiple(
            () => Assert.False(change.FileChanged),
            () => Assert.Equal(["values.altText"], change.Changes)
        );
    }

    [Fact]
    public void Compare_LiveItemNotInSnapshot_IsRemoved()
    {
        var id = Guid.NewGuid();

        var diff = MediaDiffEngine.Compare(new MediaSnapshot(), [Live(id, null, Folder(id))]);

        Assert.Equal(id, Assert.Single(diff.Removed).Id);
    }

    [Fact]
    public void Compare_OnlyTheParentDiffers_IsDrifted()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo, parent: Guid.NewGuid());

        var diff = MediaDiffEngine.Compare(
            snapshot,
            [Live(id, null, Image(id, "/media/other/photo.jpg", Photo.Length))]
        );

        Assert.Equal(ContentChangeKind.Drifted, Assert.Single(diff.Items).Change);
    }

    // ── apply ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_NewImage_StagesItsFileAndCreatesWithTheSameId()
    {
        // Arrange
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo, parent);
        var fake = new FakeUmbracoManagementClient();
        var diff = MediaDiffEngine.Compare(snapshot, []);

        // Act
        var result = await MediaApplier.ApplyAsync(
            fake,
            snapshot,
            diff,
            false,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var staged = Assert.Single(fake.StagedFiles);
        var sent = Assert.Single(fake.RawWrites).Body;
        var file = sent["values"]!.AsArray().Single(v => (string?)v!["alias"] == "umbracoFile")![
            "value"
        ]!;
        Assert.Multiple(
            () => Assert.Equal(Photo, staged.Content),
            () => Assert.Equal(id.ToString(), (string?)sent["id"]),
            () => Assert.Equal(parent.ToString(), (string?)sent["parent"]?["id"]),
            () => Assert.Equal(staged.Id.ToString(), (string?)file["temporaryFileId"]),
            () => Assert.Null(file["src"])
        );
    }

    [Fact]
    public async Task ApplyAsync_NewFolder_CreatesWithoutStaging()
    {
        var id = Guid.NewGuid();
        var snapshot = new MediaSnapshot
        {
            Directory = _dir,
            Items = [new MediaNode { Id = id, Body = Folder(id) }],
        };
        var fake = new FakeUmbracoManagementClient();

        await MediaApplier.ApplyAsync(
            fake,
            snapshot,
            MediaDiffEngine.Compare(snapshot, []),
            false,
            false,
            CancellationToken.None
        );

        Assert.Multiple(() => Assert.Empty(fake.StagedFiles), () => Assert.Single(fake.RawWrites));
    }

    [Fact]
    public async Task ApplyAsync_BodyOnlyChange_KeepsTheLiveFile()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);
        var live = Image(id, "/media/live/photo.jpg", Photo.Length, alt: "Old");
        var fake = new FakeUmbracoManagementClient();

        await MediaApplier.ApplyAsync(
            fake,
            snapshot,
            MediaDiffEngine.Compare(snapshot, [Live(id, null, live)]),
            false,
            false,
            CancellationToken.None
        );

        var write = Assert.Single(fake.RawWrites);
        Assert.Multiple(
            () => Assert.Empty(fake.StagedFiles),
            () => Assert.Equal(id, write.Id),
            () => Assert.Equal("/media/live/photo.jpg", MediaBody.SrcOf(write.Body))
        );
    }

    [Fact]
    public async Task ApplyAsync_ChangedFile_StagesTheSnapshotFile()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);
        var fake = new FakeUmbracoManagementClient();

        await MediaApplier.ApplyAsync(
            fake,
            snapshot,
            MediaDiffEngine.Compare(snapshot, [Live(id, null, Image(id, "/media/l/photo.jpg", 1))]),
            false,
            false,
            CancellationToken.None
        );

        Assert.Equal(Photo, Assert.Single(fake.StagedFiles).Content);
    }

    [Fact]
    public async Task ApplyAsync_DryRun_StagesAndWritesNothing()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);
        var fake = new FakeUmbracoManagementClient();

        var result = await MediaApplier.ApplyAsync(
            fake,
            snapshot,
            MediaDiffEngine.Compare(snapshot, []),
            false,
            true,
            CancellationToken.None
        );

        Assert.Multiple(
            () => Assert.Equal("planned", Assert.Single(result.Data!.Actions).Status),
            () => Assert.Empty(fake.StagedFiles),
            () => Assert.Empty(fake.RawWrites)
        );
    }

    [Fact]
    public async Task ApplyAsync_Prune_TrashesChildrenFirstAndSparesAncestorsOfKeptItems()
    {
        // Arrange: live Old/Older (both gone from the snapshot) and Kept/Stray, where Kept is
        // still in the snapshot but its parent Shelf is not.
        var old = Guid.NewGuid();
        var older = Guid.NewGuid();
        var shelf = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var snapshot = new MediaSnapshot
        {
            Directory = _dir,
            Items =
            [
                new MediaNode
                {
                    Id = kept,
                    Parent = shelf,
                    Body = Folder(kept),
                },
            ],
        };
        var live = new List<MediaNode>
        {
            Live(old, null, Folder(old)),
            Live(older, old, Folder(older)),
            Live(shelf, null, Folder(shelf)),
            Live(kept, shelf, snapshot.Items[0].Body),
        };
        var fake = new FakeUmbracoManagementClient();

        // Act
        await MediaApplier.ApplyAsync(
            fake,
            snapshot,
            MediaDiffEngine.Compare(snapshot, live),
            true,
            false,
            CancellationToken.None
        );

        // Assert
        Assert.Equal([older, old], fake.MediaTrashed);
    }

    [Fact]
    public async Task ApplyAsync_FileMissingFromSnapshot_Fails()
    {
        var id = Guid.NewGuid();
        var snapshot = SnapshotWithImage(id, Photo);
        File.Delete(snapshot.PathOf(snapshot.Items[0].File!));

        var result = await MediaApplier.ApplyAsync(
            new FakeUmbracoManagementClient(),
            snapshot,
            MediaDiffEngine.Compare(snapshot, []),
            false,
            false,
            CancellationToken.None
        );

        Assert.Contains("missing the file", result.ErrorMessage);
    }
}
