using System.Text.Json;

namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// Effect tests (#187 Phase 2): do the thing, then <b>re-read the instance</b> and assert the state
/// actually changed.
/// <para>
/// Every other test in this harness asserts an exit code, an envelope shape, or that something was
/// NOT mutated. That is exactly the gap #158 fell through: <c>content publish</c> returned exit 0
/// and <c>{"status":"success"}</c> for two releases while publishing nothing, and no test noticed,
/// because none of them looked at the document afterwards. An assertion that stops at "the command
/// succeeded" cannot catch a command that lies.
/// </para>
/// <para>
/// Which follows a rule these tests keep strictly: <c>Skip</c> is for the <i>environment</i> - no
/// reachable instance, or an instance that will not accept a fixture at all. Anything the harness
/// controls is an <c>Assert</c>. Skipping because a command under test did not do what it claimed
/// would turn the regression into a green result, which is the failure mode this whole file exists
/// to close.
/// </para>
/// <para>
/// Skipped when no instance is reachable, like the rest of the harness. Until #77 lands there is no
/// way to provision one, so these skip everywhere including locally - they are written now so the
/// behaviour recorded on #158 and #178 is pinned in code while it is fresh.
/// </para>
/// </summary>
/// <param name="live">Shared reachability fixture.</param>
[Collection("Live")]
public sealed class EffectIntegrationTests(LiveInstanceFixture live) : LiveTestBase(live)
{
    [SkippableFact]
    public void Publish_ThenReRead_ReportsPublished()
    {
        RequireLive();

        // #158: the regression this file exists to catch. Before the fix this command returned
        // exit 0 and {"status":"success"} while the draft stayed unpublished, because the request
        // carried an empty `schedule` object that Umbraco silently ignores.
        using var doc = ScratchDocument.Create("clitest publish effect");

        Assert.False(doc.IsPublished(), "A freshly created document should be a draft.");

        var publish = CliRunner.Run("content", "publish", doc.Id);
        Assert.True(publish.Ok, publish.Stderr);

        Assert.True(
            doc.IsPublished(),
            "content publish reported success but the document is still unpublished (#158)."
        );
    }

    [SkippableFact]
    public void Unpublish_ThenReRead_ReportsNotPublished()
    {
        RequireLive();

        // #149: unpublish omits the cultures field rather than sending ["*"], which 400s on
        // invariant content. The effect, not the exit code, is what proves it.
        using var doc = ScratchDocument.Create("clitest unpublish effect");

        var publish = CliRunner.Run("content", "publish", doc.Id);
        Assert.True(publish.Ok, publish.Stderr);
        Assert.True(doc.IsPublished(), "Could not publish the fixture document (#158).");

        var unpublish = CliRunner.Run("content", "unpublish", doc.Id, "--yes");
        Assert.True(unpublish.Ok, unpublish.Stderr);

        Assert.False(
            doc.IsPublished(),
            "content unpublish reported success but the document is still published."
        );
    }

    [SkippableFact]
    public void BulkPublish_ThenReRead_ReportsPublished()
    {
        RequireLive();

        // #158 affected `content bulk publish` too, and it reported per-item success for every id
        // while publishing none of them.
        using var doc = ScratchDocument.Create("clitest bulk publish effect");

        var bulk = CliRunner.RunWithInput(doc.Id + "\n", "content", "bulk", "publish");
        Assert.True(bulk.Ok, bulk.Stderr);

        Assert.True(
            doc.IsPublished(),
            "content bulk publish reported per-item success but nothing was published (#158)."
        );
    }

    [SkippableFact]
    public void Update_ThenExport_KeepsTheTemplate()
    {
        RequireLive();

        // #178: `content update` sent only values and variants, so the replace-semantics PUT
        // cleared the item's template and every page 404'd once republished. The exit code was 0
        // throughout - only re-reading the document shows it.
        //
        // UNVERIFIED until this runs against a live instance: that `content create --template`
        // accepts an alias (rather than requiring an id), and that the exported body carries the
        // template as `template.id`. Both are raw Management API surface that this repo does not
        // model, so the first live run should check here first if this test misbehaves.
        using var template = ScratchTemplate.Create();
        using var doc = ScratchDocument.Create("clitest template effect", template.Alias);

        var before = doc.ExportBody();
        Assert.True(
            before.TryGetProperty("template", out var original)
                && original.ValueKind is not JsonValueKind.Null,
            "content create --template did not set a template, so there is nothing to preserve "
                + "- that is the same bug class as #178 one endpoint over, not a reason to skip."
        );

        // A name-only update: it says nothing about the template, so the template must survive.
        var update = CliRunner.RunWithInput(
            """{"variants":[{"name":"clitest template effect renamed"}]}""",
            "content",
            "update",
            doc.Id,
            "--json-body",
            "-"
        );
        Assert.True(update.Ok, update.Stderr);

        var after = doc.ExportBody();
        Assert.True(
            after.TryGetProperty("template", out var kept)
                && kept.ValueKind is not JsonValueKind.Null,
            "content update cleared the document's template (#178)."
        );
        Assert.Equal(template.Id, kept.GetProperty("id").GetString());
    }

    [SkippableFact]
    public void Apply_RestoresPublishStateThenDiffAndASecondApplyFindNothing()
    {
        RequireLive();

        // #223 + #224 on one instance: the snapshot has the document published; it is then
        // unpublished live, so apply must publish it back. After that the instance matches the
        // snapshot, so diff must be empty and a second apply must do nothing - before the
        // normaliser both reported every document as changed. (The cross-instance version of
        // this needs a second instance, which the harness cannot provision yet, #77.)
        using var doc = ScratchDocument.Create("clitest promotion effect");
        Assert.True(CliRunner.Run("content", "publish", doc.Id).Ok, "fixture publish failed");
        var snapshot = Path.Combine(Path.GetTempPath(), $"clitest-{Guid.NewGuid():N}.json");
        try
        {
            var export = CliRunner.Run("content", "export", "--root", doc.Id, "--out", snapshot);
            Assert.True(export.Ok, export.Stderr);
            var unpublish = CliRunner.Run("content", "unpublish", doc.Id, "--yes");
            Assert.True(unpublish.Ok, unpublish.Stderr);

            var apply = CliRunner.Run("content", "apply", snapshot);
            Assert.True(apply.Ok, apply.Stderr);
            Assert.True(
                doc.IsPublished(),
                "content apply did not restore the publish state (#223)."
            );

            var diff = CliRunner.Run("content", "diff", snapshot);
            Assert.True(diff.Ok, diff.Stderr);
            Assert.Equal(0, Data(diff).GetArrayLength());

            var again = CliRunner.Run("content", "apply", snapshot);
            Assert.True(again.Ok, again.Stderr);
            Assert.Equal(0, Data(again).GetArrayLength());
        }
        finally
        {
            File.Delete(snapshot);
        }
    }

    [SkippableFact]
    public void Apply_RecreatesDeletedVariantDocumentAndRestoresDriftedValues()
    {
        RequireLive();

        // #103: the POST (create) half of the export -> apply contract, on a multi-culture
        // subtree. The snapshot carries each document's verbatim GET body - variant `state` and
        // dates included - and apply sends it back. If Umbraco's create or update request models
        // rejected that response shape, apply would only fail here, against a real server.
        //
        // Arrange: root (published in both cultures) with one child published in en-US only,
        // so the snapshot has mixed per-culture publish state as well as per-culture values.
        using var type = ScratchCultureType.Create();
        var rootId = type.CreateDocument(
            null,
            ("en-US", "clitest apply root", "Root hello"),
            ("da-DK", "clitest apply rod", "Rod hej")
        );
        var childId = type.CreateDocument(
            rootId,
            ("en-US", "clitest apply child", "Child hello"),
            ("da-DK", "clitest apply barn", "Barn hej")
        );
        Assert.True(CliRunner.Run("content", "publish", rootId).Ok, "fixture publish failed");
        Assert.True(
            CliRunner.Run("content", "publish", childId, "--culture", "en-US").Ok,
            "fixture en-US publish failed"
        );
        var snapshot = Path.Combine(Path.GetTempPath(), $"clitest-{Guid.NewGuid():N}.json");
        try
        {
            var export = CliRunner.Run("content", "export", "--root", rootId, "--out", snapshot);
            Assert.True(export.Ok, export.Stderr);

            // Drift the live subtree: the child goes (so apply must POST it back under its
            // parent), and the root's Danish value changes (so apply must PUT it back).
            var delete = CliRunner.Run("content", "delete", childId, "--yes");
            Assert.True(delete.Ok, delete.Stderr);
            var drift = CliRunner.RunWithInput(
                $$"""{"values":[{"alias":"{{ScratchCultureType.PropertyAlias}}","culture":"da-DK","value":"Drifted"}]}""",
                "content",
                "update",
                rootId,
                "--json-body",
                "-"
            );
            Assert.True(drift.Ok, drift.Stderr);

            // Act
            var apply = CliRunner.Run("content", "apply", snapshot);

            // Assert: the child is back with both variants, their names, values and per-culture
            // publish state; the root has its snapshot value again.
            Assert.True(apply.Ok, apply.Stderr);
            List<(string?, string?, string?, string?)> expected =
            [
                ("en-US", "clitest apply child", "Published", "Child hello"),
                ("da-DK", "clitest apply barn", "Draft", "Barn hej"),
            ];
            Assert.Equal(expected, Variants(ExportBody(rootId, childId)));
            Assert.Equal("Rod hej", Value(ExportBody(rootId, rootId), "da-DK"));

            // The instance now matches the snapshot, so there is nothing left to diff or apply.
            var diff = CliRunner.Run("content", "diff", snapshot);
            Assert.True(diff.Ok, diff.Stderr);
            Assert.Equal(0, Data(diff).GetArrayLength());
            var again = CliRunner.Run("content", "apply", snapshot);
            Assert.True(again.Ok, again.Stderr);
            Assert.Equal(0, Data(again).GetArrayLength());
        }
        finally
        {
            File.Delete(snapshot);
        }
    }

    [SkippableFact]
    public void ApplyPrune_DeletesOnlyTheInScopeDocumentTheSnapshotLacks()
    {
        RequireLive();

        // #103: `apply --prune` on a subtree snapshot deletes live documents inside that subtree
        // which the snapshot does not contain - and nothing outside it. The snapshot records its
        // root, and diff/apply export the live tree at that same root; if they did not, every
        // other document on the instance would read as "removed" and be deleted.
        //
        // Arrange: snapshot a root + child, then add a second child (in scope, not in the
        // snapshot) and a second root document (out of scope).
        using var type = ScratchCultureType.Create();
        var rootId = type.CreateDocument(null, ("en-US", "clitest prune root", "Root"));
        var keptId = type.CreateDocument(rootId, ("en-US", "clitest prune kept", "Kept"));
        var snapshot = Path.Combine(Path.GetTempPath(), $"clitest-{Guid.NewGuid():N}.json");
        try
        {
            var export = CliRunner.Run("content", "export", "--root", rootId, "--out", snapshot);
            Assert.True(export.Ok, export.Stderr);
            var extraId = type.CreateDocument(rootId, ("en-US", "clitest prune extra", "Extra"));
            var outsideId = type.CreateDocument(null, ("en-US", "clitest prune outside", "Out"));

            // Act
            var prune = CliRunner.Run("content", "apply", snapshot, "--prune", "--yes");

            // Assert: only the in-scope extra went; the snapshot's documents and the
            // out-of-scope document are untouched.
            Assert.True(prune.Ok, prune.Stderr);
            Assert.True(
                IsNotFound(CliRunner.Run("content", "get", extraId)),
                "apply --prune left the in-scope document the snapshot does not contain."
            );
            Assert.True(CliRunner.Run("content", "get", rootId).Ok, "prune deleted the root");
            Assert.True(CliRunner.Run("content", "get", keptId).Ok, "prune deleted a kept child");
            Assert.True(
                CliRunner.Run("content", "get", outsideId).Ok,
                "apply --prune deleted a document outside the snapshot's scope."
            );
        }
        finally
        {
            File.Delete(snapshot);
        }
    }

    /// <summary>
    /// One document's verbatim body, read back through a <c>content export</c> of its subtree
    /// (<c>content get</c> is a narrow projection without values or per-variant state).
    /// </summary>
    /// <param name="rootId">The subtree root to export.</param>
    /// <param name="id">The document to pick out of the export.</param>
    /// <returns>The document's body.</returns>
    private static JsonElement ExportBody(string rootId, string id)
    {
        var export = CliRunner.Run("content", "export", "--root", rootId);
        Assert.True(export.Ok, export.Stderr);
        return export
            .Data()
            .GetProperty("documents")
            .EnumerateArray()
            .Single(d => d.GetProperty("id").GetString() == id)
            .GetProperty("body");
    }

    /// <summary>
    /// A document's variants as (culture, name, publish state, <c>title</c> value) rows, in the
    /// order the body lists them, so a whole multi-variant document compares in one assertion.
    /// </summary>
    /// <param name="body">A document body from <see cref="ExportBody"/>.</param>
    /// <returns>One row per variant.</returns>
    private static List<(string?, string?, string?, string?)> Variants(JsonElement body) =>
        [
            .. body.GetProperty("variants")
                .EnumerateArray()
                .Select(v =>
                {
                    var culture = v.GetProperty("culture").GetString();
                    return (
                        culture,
                        v.GetProperty("name").GetString(),
                        v.GetProperty("state").GetString(),
                        Value(body, culture!)
                    );
                }),
        ];

    /// <summary>A document's <see cref="ScratchCultureType.PropertyAlias"/> value in one culture.</summary>
    /// <param name="body">A document body from <see cref="ExportBody"/>.</param>
    /// <param name="culture">The culture to read.</param>
    /// <returns>The value, or null when the document has none in that culture.</returns>
    private static string? Value(JsonElement body, string culture) =>
        body.GetProperty("values")
            .EnumerateArray()
            .Where(v =>
                v.GetProperty("alias").GetString() == ScratchCultureType.PropertyAlias
                && v.GetProperty("culture").GetString() == culture
            )
            .Select(v => v.GetProperty("value").GetString())
            .SingleOrDefault();

    /// <summary>The <c>data</c> array of a command's JSON envelope.</summary>
    private static JsonElement Data(CliResult result) =>
        JsonDocument.Parse(result.Stdout).RootElement.GetProperty("data");

    [SkippableFact]
    public void Update_ThenExport_RenamesWithoutAddingAVariant()
    {
        RequireLive();

        // The merge matches variants on culture + segment, so renaming an invariant document must
        // change the existing variant rather than appending a second one.
        using var doc = ScratchDocument.Create("clitest rename effect");

        var update = CliRunner.RunWithInput(
            """{"variants":[{"name":"clitest renamed"}]}""",
            "content",
            "update",
            doc.Id,
            "--json-body",
            "-"
        );
        Assert.True(update.Ok, update.Stderr);

        var variants = doc.ExportBody().GetProperty("variants");
        Assert.Equal(1, variants.GetArrayLength());
        Assert.Equal("clitest renamed", variants[0].GetProperty("name").GetString());
    }
}
