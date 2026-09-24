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
