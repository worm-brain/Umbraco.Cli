namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// End-to-end integration tests (issue #51): drive the real CLI against a live Umbraco and
/// assert list/get/create/delete behaviour across the command surface. Skipped when no
/// instance is reachable (see <see cref="LiveInstanceFixture"/>).
/// </summary>
[Collection("Live")]
public sealed class CommandIntegrationTests
{
    private readonly LiveInstanceFixture _live;

    /// <param name="live">Shared reachability fixture.</param>
    public CommandIntegrationTests(LiveInstanceFixture live) => _live = live;

    /// <summary>Skips the current test unless a live instance responded to the probe.</summary>
    private void RequireLive() => Skip.IfNot(_live.IsReachable, _live.SkipReason);

    [SkippableFact]
    public void Whoami_ReturnsAuthenticatedIdentity()
    {
        RequireLive();
        var result = CliRunner.Run("auth", "whoami");

        Assert.True(result.Ok, result.Stderr);
        Assert.False(string.IsNullOrWhiteSpace(result.Data().GetProperty("userName").GetString()));
    }

    [SkippableFact]
    public void ContentList_ReturnsItems()
    {
        RequireLive();
        var result = CliRunner.Run("content", "list", "--take", "5");

        Assert.True(result.Ok, result.Stderr);
        Assert.Equal(JsonValueKind.Array, result.Data().ValueKind);
    }

    [SkippableFact]
    public void ContentGet_ReturnsRealName()
    {
        RequireLive();
        var list = CliRunner.Run("content", "list", "--take", "1");
        Skip.If(list.Data().GetArrayLength() == 0, "No content in the instance to get.");

        var id = list.Data()[0].GetProperty("ID").GetString()!;
        var get = CliRunner.Run("content", "get", id);

        Assert.True(get.Ok, get.Stderr);
        // Regression guard for #42: name comes from variants[], must not be empty.
        Assert.False(string.IsNullOrWhiteSpace(get.Data().GetProperty("name").GetString()));
    }

    [SkippableFact]
    public void MediaList_ReturnsItems()
    {
        RequireLive();
        Assert.True(CliRunner.Run("media", "list", "--take", "5").Ok);
    }

    [SkippableFact]
    public void ContentTypesList_ReturnsItems()
    {
        RequireLive();
        Assert.True(CliRunner.Run("content-types", "list", "--take", "5").Ok);
    }

    [SkippableFact]
    public void DataTypesList_ReturnsItems()
    {
        RequireLive();
        Assert.True(CliRunner.Run("data-types", "list", "--take", "5").Ok);
    }

    [SkippableFact]
    public void TemplatesList_ReturnsItems()
    {
        RequireLive();
        Assert.True(CliRunner.Run("templates", "list", "--take", "5").Ok);
    }

    [SkippableFact]
    public void MembersList_DefaultPaging_DoesNotError()
    {
        RequireLive();
        // Regression guard for #39: a bare `members list` (no --take) must not 500.
        var result = CliRunner.Run("members", "list");
        Assert.True(result.Ok, result.Stderr);
    }

    [SkippableFact]
    public void LanguagesList_ReturnsItems()
    {
        RequireLive();
        var result = CliRunner.Run("languages", "list");
        Assert.True(result.Ok, result.Stderr);
        Assert.Equal(JsonValueKind.Array, result.Data().ValueKind);
    }

    [SkippableFact]
    public void Language_CreateDelete_RoundTrips()
    {
        RequireLive();

        // Pick a valid BCP-47 culture that is NOT already configured, so this is a genuine
        // new-language create rather than an "already exists" 400. The instance ships with a
        // few languages, so probe the list and take the first spare candidate.
        var existing = CliRunner.Run("languages", "list");
        Assert.True(existing.Ok, existing.Stderr);
        var iso = new[] { "fr-CA", "es-MX", "de-AT", "pt-BR", "en-AU", "nl-BE" }.FirstOrDefault(c =>
            !existing.Stdout.Contains($"\"{c}\"")
        );
        Skip.If(iso is null, "No spare test culture available on this instance.");

        // Create (now on the generated client). The create echoes the accepted request, so
        // isoCode/name come back populated rather than blank (guards #74 for this resource).
        var create = CliRunner.Run(
            "languages",
            "create",
            "--culture",
            iso,
            "--name",
            $"Integration test {iso}"
        );
        Assert.True(create.Ok, create.Stderr);
        try
        {
            // The create response is the raw LanguageResponse: { isoCode, name, ... }.
            Assert.Equal(iso, create.Data().GetProperty("isoCode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(create.Data().GetProperty("name").GetString()));
        }
        finally
        {
            // Delete (self-clean) - always runs once the create succeeded, even if an
            // assertion above fails, so a failing run never leaks a language. A successful
            // delete also proves the create really persisted (deleting a missing iso fails).
            // --yes: delete is destructive and the harness runs non-interactively (#70).
            var delete = CliRunner.Run("languages", "delete", iso, "--yes");
            Assert.True(delete.Ok, delete.Stderr);
        }
    }

    [SkippableFact]
    public void DictionaryList_ReturnsItems()
    {
        RequireLive();
        Assert.True(CliRunner.Run("dictionary", "list", "--take", "5").Ok);
    }

    [SkippableFact]
    public void WebhooksList_ReturnsItems()
    {
        RequireLive();
        Assert.True(CliRunner.Run("webhooks", "list", "--take", "5").Ok);
    }

    [SkippableFact]
    public void DryRun_Create_PreviewsRequestAndDoesNotMutate()
    {
        RequireLive();
        const string marker = "https://example.com/dry-run-must-not-exist";

        // A create with --dry-run must return a dry-run preview of the POST and change nothing.
        var dry = CliRunner.Run(
            "webhooks",
            "create",
            "--url",
            marker,
            "--events",
            "ContentPublished",
            "--dry-run"
        );
        Assert.True(dry.Ok, dry.Stderr);

        using var doc = JsonDocument.Parse(dry.Stdout);
        Assert.Equal("dry-run", doc.RootElement.GetProperty("status").GetString());
        var request = doc.RootElement.GetProperty("request");
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.Contains("webhook", request.GetProperty("url").GetString());

        // Nothing should have been created - the marker URL must not appear in the list.
        var list = CliRunner.Run("webhooks", "list", "--take", "100");
        Assert.True(list.Ok, list.Stderr);
        Assert.DoesNotContain(marker, list.Stdout);
    }

    [SkippableFact]
    public void ReadOnly_BlocksWriteButAllowsRead()
    {
        RequireLive();

        // A read still works under --readonly.
        var list = CliRunner.Run("content", "list", "--take", "1", "--readonly");
        Assert.True(list.Ok, list.Stderr);

        // A write is refused (exit 2) and nothing is created.
        const string marker = "https://example.com/readonly-should-not-create";
        var create = CliRunner.Run(
            "webhooks",
            "create",
            "--url",
            marker,
            "--events",
            "ContentPublished",
            "--readonly"
        );
        Assert.Equal(2, create.ExitCode);
        var hooks = CliRunner.Run("webhooks", "list", "--take", "100");
        Assert.DoesNotContain(marker, hooks.Stdout);
    }

    [SkippableFact]
    public void AllowList_RestrictsCommandSurface()
    {
        RequireLive();
        // The child CLI inherits this process's environment; the Live collection runs
        // sequentially, so setting/clearing it around this one test is safe.
        Environment.SetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS", "content");
        try
        {
            // In-list group runs.
            Assert.True(CliRunner.Run("content", "list", "--take", "1").Ok);
            // Out-of-list command is refused before running (exit 2).
            Assert.Equal(2, CliRunner.Run("webhooks", "list", "--take", "1").ExitCode);
            // auth stays allowed regardless of the allow-list.
            Assert.True(CliRunner.Run("auth", "whoami").Ok);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMBRACO_ALLOWED_COMMANDS", null);
        }
    }

    [SkippableFact]
    public void DestructiveDelete_WithoutYes_RefusedNonInteractively()
    {
        RequireLive();
        // Create a webhook to attempt deleting.
        var create = CliRunner.Run(
            "webhooks",
            "create",
            "--url",
            "https://example.com/confirm-gate-test",
            "--events",
            "ContentPublished"
        );
        Assert.True(create.Ok, create.Stderr);
        var id = create.Data().GetProperty("id").GetString()!;

        try
        {
            // Delete without --yes: the harness runs non-interactively, so the destructive-op
            // gate (#70) must refuse (exit 2) and the webhook must still exist.
            var refused = CliRunner.Run("webhooks", "delete", id);
            Assert.Equal(2, refused.ExitCode);
            var list = CliRunner.Run("webhooks", "list", "--take", "100");
            Assert.Contains(id, list.Stdout);
        }
        finally
        {
            // Clean up with --yes (the sanctioned bypass).
            CliRunner.Run("webhooks", "delete", id, "--yes");
        }
    }

    [SkippableFact]
    public void Webhook_CreateListDelete_RoundTrips()
    {
        RequireLive();

        // Create
        var create = CliRunner.Run(
            "webhooks",
            "create",
            "--url",
            "https://example.com/integration-test-hook",
            "--events",
            "ContentPublished"
        );
        Assert.True(create.Ok, create.Stderr);
        var id = create.Data().GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));

        try
        {
            // List should now include the new webhook.
            var list = CliRunner.Run("webhooks", "list", "--take", "100");
            Assert.True(list.Ok, list.Stderr);
            var found = list.Data()
                .EnumerateArray()
                .Any(w => w.GetProperty("ID").GetString() == id);
            Assert.True(found, "Created webhook was not present in the list.");
        }
        finally
        {
            // Delete (self-clean) - runs even if the assertions above fail.
            // --yes: delete is destructive and the harness runs non-interactively (#70).
            var delete = CliRunner.Run("webhooks", "delete", id!, "--yes");
            Assert.True(delete.Ok, delete.Stderr);
        }
    }
}
