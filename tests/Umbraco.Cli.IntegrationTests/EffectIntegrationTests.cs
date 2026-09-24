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
/// Skipped when no instance is reachable, like the rest of the harness. Until #77 lands there is no
/// way to provision one, so these skip everywhere including locally - they are written now so the
/// behaviour recorded on #158 and #178 is pinned in code while it is fresh.
/// </para>
/// </summary>
[Collection("Live")]
public sealed class EffectIntegrationTests
{
    private readonly LiveInstanceFixture _live;

    /// <param name="live">Shared reachability fixture.</param>
    public EffectIntegrationTests(LiveInstanceFixture live) => _live = live;

    /// <summary>Skips the current test unless a live instance responded to the probe.</summary>
    private void RequireLive() => Skip.IfNot(_live.IsReachable, _live.SkipReason);

    /// <summary>A document type that allows creation at the root, with a random alias.</summary>
    /// <returns>The created type's id and alias.</returns>
    private static (string Id, string Alias) CreateRootDocumentType()
    {
        var alias = "clitestEffect" + Guid.NewGuid().ToString("N")[..8];
        var created = CliRunner.Run(
            "content-types",
            "create",
            "--name",
            alias,
            "--alias",
            alias,
            "--allow-at-root"
        );
        Assert.True(created.Ok, created.Stderr);
        return (created.Data().GetProperty("id").GetString()!, alias);
    }

    /// <summary>Reads a document's verbatim body back through <c>content export</c>.</summary>
    /// <remarks>
    /// <c>content get</c> still returns a narrow projection (#168, Phase 3), so the only way to see
    /// a document's template and property values through the CLI is the export snapshot, which
    /// carries the body as the Management API returned it.
    /// </remarks>
    /// <param name="id">The document id.</param>
    /// <returns>The document's body.</returns>
    private static JsonElement ExportBody(string id)
    {
        var export = CliRunner.Run("content", "export", "--root", id);
        Assert.True(export.Ok, export.Stderr);
        var documents = export.Data().GetProperty("documents");
        Assert.True(documents.GetArrayLength() > 0, "The export returned no documents.");
        return documents[0].GetProperty("body");
    }

    /// <summary>Whether the document currently reports as published.</summary>
    /// <param name="id">The document id.</param>
    /// <returns>True when <c>content get</c> reports it published.</returns>
    private static bool IsPublished(string id)
    {
        var get = CliRunner.Run("content", "get", id);
        Assert.True(get.Ok, get.Stderr);
        return get.Data().GetProperty("isPublished").GetBoolean();
    }

    [SkippableFact]
    public void Publish_ThenReRead_ReportsPublished()
    {
        RequireLive();

        // #158: the regression this exists to catch. Before the fix this command returned exit 0
        // and {"status":"success"} while the draft stayed unpublished, because the request carried
        // an empty `schedule` object that Umbraco silently ignores. Exit code alone is not a pass.
        var (docTypeId, alias) = CreateRootDocumentType();
        string? contentId = null;
        try
        {
            var create = CliRunner.Run(
                "content",
                "create",
                "--content-type",
                alias,
                "--name",
                "clitest publish effect"
            );
            Assert.True(create.Ok, create.Stderr);
            contentId = create.Data().GetProperty("id").GetString();
            Assert.False(IsPublished(contentId!), "A freshly created document should be a draft.");

            var publish = CliRunner.Run("content", "publish", contentId!);
            Assert.True(publish.Ok, publish.Stderr);

            Assert.True(
                IsPublished(contentId!),
                "content publish reported success but the document is still unpublished (#158)."
            );
        }
        finally
        {
            if (contentId is not null)
                CliRunner.Run("content", "delete", contentId, "--yes");
            CliRunner.Run("content-types", "delete", docTypeId, "--yes");
        }
    }

    [SkippableFact]
    public void Unpublish_ThenReRead_ReportsNotPublished()
    {
        RequireLive();

        // #149: unpublish omits the cultures field rather than sending ["*"], which 400s on
        // invariant content. The effect, not the exit code, is what proves it.
        var (docTypeId, alias) = CreateRootDocumentType();
        string? contentId = null;
        try
        {
            var create = CliRunner.Run(
                "content",
                "create",
                "--content-type",
                alias,
                "--name",
                "clitest unpublish effect"
            );
            Assert.True(create.Ok, create.Stderr);
            contentId = create.Data().GetProperty("id").GetString();

            Assert.True(CliRunner.Run("content", "publish", contentId!).Ok);
            Skip.IfNot(IsPublished(contentId!), "Could not publish the fixture document.");

            var unpublish = CliRunner.Run("content", "unpublish", contentId!, "--yes");
            Assert.True(unpublish.Ok, unpublish.Stderr);

            Assert.False(
                IsPublished(contentId!),
                "content unpublish reported success but the document is still published."
            );
        }
        finally
        {
            if (contentId is not null)
                CliRunner.Run("content", "delete", contentId, "--yes");
            CliRunner.Run("content-types", "delete", docTypeId, "--yes");
        }
    }

    [SkippableFact]
    public void BulkPublish_ThenReRead_ReportsPublished()
    {
        RequireLive();

        // #158 affected `content bulk publish` too, and it reported per-item success for every id
        // while publishing none of them.
        var (docTypeId, alias) = CreateRootDocumentType();
        string? contentId = null;
        try
        {
            var create = CliRunner.Run(
                "content",
                "create",
                "--content-type",
                alias,
                "--name",
                "clitest bulk publish effect"
            );
            Assert.True(create.Ok, create.Stderr);
            contentId = create.Data().GetProperty("id").GetString();

            var bulk = CliRunner.RunWithInput(contentId! + "\n", "content", "bulk", "publish");
            Assert.True(bulk.Ok, bulk.Stderr);

            Assert.True(
                IsPublished(contentId!),
                "content bulk publish reported per-item success but nothing was published (#158)."
            );
        }
        finally
        {
            if (contentId is not null)
                CliRunner.Run("content", "delete", contentId, "--yes");
            CliRunner.Run("content-types", "delete", docTypeId, "--yes");
        }
    }

    [SkippableFact]
    public void Update_ThenExport_KeepsTheTemplate()
    {
        RequireLive();

        // #178: `content update` sent only values and variants, so the replace-semantics PUT
        // cleared the item's template and every page 404'd once republished. The exit code was 0
        // throughout - only re-reading the document shows it.
        var (docTypeId, alias) = CreateRootDocumentType();
        var templateAlias = "clitestTpl" + Guid.NewGuid().ToString("N")[..8];
        var template = CliRunner.Run(
            "templates",
            "create",
            "--name",
            templateAlias,
            "--alias",
            templateAlias
        );
        Skip.IfNot(template.Ok, $"Could not create a template: {template.Stderr}");
        var templateId = template.Data().GetProperty("id").GetString();

        string? contentId = null;
        try
        {
            var create = CliRunner.Run(
                "content",
                "create",
                "--content-type",
                alias,
                "--name",
                "clitest template effect",
                "--template",
                templateAlias
            );
            Skip.IfNot(
                create.Ok,
                $"Could not create content with a template on this instance: {create.Stderr}"
            );
            contentId = create.Data().GetProperty("id").GetString();

            Skip.IfNot(
                ExportBody(contentId!).TryGetProperty("template", out var before)
                    && before.ValueKind is not JsonValueKind.Null,
                "The fixture document was created without a template, so there is nothing to preserve."
            );

            // A name-only update: it says nothing about the template, so the template must survive.
            var update = CliRunner.RunWithInput(
                """{"variants":[{"name":"clitest template effect renamed"}]}""",
                "content",
                "update",
                contentId!,
                "--json-body",
                "-"
            );
            Assert.True(update.Ok, update.Stderr);

            var body = ExportBody(contentId!);
            Assert.True(
                body.TryGetProperty("template", out var after)
                    && after.ValueKind is not JsonValueKind.Null,
                "content update cleared the document's template (#178)."
            );
            Assert.Equal(templateId, after.GetProperty("id").GetString());
        }
        finally
        {
            if (contentId is not null)
                CliRunner.Run("content", "delete", contentId, "--yes");
            CliRunner.Run("content-types", "delete", docTypeId, "--yes");
            CliRunner.Run("templates", "delete", templateId!, "--yes");
        }
    }

    [SkippableFact]
    public void Update_ThenExport_RenamesWithoutAddingAVariant()
    {
        RequireLive();

        // The merge matches variants on culture + segment, so renaming an invariant document must
        // change the existing variant rather than appending a second one.
        var (docTypeId, alias) = CreateRootDocumentType();
        string? contentId = null;
        try
        {
            var create = CliRunner.Run(
                "content",
                "create",
                "--content-type",
                alias,
                "--name",
                "clitest rename effect"
            );
            Assert.True(create.Ok, create.Stderr);
            contentId = create.Data().GetProperty("id").GetString();

            var update = CliRunner.RunWithInput(
                """{"variants":[{"name":"clitest renamed"}]}""",
                "content",
                "update",
                contentId!,
                "--json-body",
                "-"
            );
            Assert.True(update.Ok, update.Stderr);

            var variants = ExportBody(contentId!).GetProperty("variants");
            Assert.Equal(1, variants.GetArrayLength());
            Assert.Equal("clitest renamed", variants[0].GetProperty("name").GetString());
        }
        finally
        {
            if (contentId is not null)
                CliRunner.Run("content", "delete", contentId, "--yes");
            CliRunner.Run("content-types", "delete", docTypeId, "--yes");
        }
    }
}
