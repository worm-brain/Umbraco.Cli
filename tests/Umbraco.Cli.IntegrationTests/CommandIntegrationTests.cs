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

        var id = list.Data()[0].GetProperty("id").GetString()!;
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

    [Fact]
    public void Schema_ContentCreate_EmitsJsonSchema()
    {
        // #61: `--schema` is local (no host/auth) and prints the raw JSON Schema of the body.
        var result = CliRunner.Run("content", "create", "--schema");

        Assert.True(result.Ok, result.Stderr);
        using var doc = JsonDocument.Parse(result.Stdout);
        // It is a bare JSON Schema document, not the CLI envelope.
        Assert.True(doc.RootElement.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("contentType", out _));
        // Root is a non-nullable object (a null body is rejected by the command).
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Schema_ContentUpdate_EmitsJsonSchema()
    {
        // update's --schema path is independent of create's, so exercise it too.
        var result = CliRunner.Run("content", "update", "--schema");

        Assert.True(result.Ok, result.Stderr);
        using var doc = JsonDocument.Parse(result.Stdout);
        var props = doc.RootElement.GetProperty("properties");
        Assert.True(props.TryGetProperty("values", out _));
        Assert.True(props.TryGetProperty("variants", out _));
    }

    [SkippableFact]
    public void Fields_ProjectsListOutput()
    {
        RequireLive();
        // #63: --fields trims each result to the listed fields (case-insensitive vs the "ID"
        // header). Requesting a single field means each item has exactly one property.
        var result = CliRunner.Run("content", "list", "--take", "5", "--fields", "id");
        Assert.True(result.Ok, result.Stderr);

        var data = result.Data();
        Skip.If(data.GetArrayLength() == 0, "No content to project.");
        foreach (var item in data.EnumerateArray())
            Assert.Equal(1, item.EnumerateObject().Count());
    }

    [SkippableFact]
    public void StdinBody_ContentCreate_ReadsPipedBody()
    {
        RequireLive();
        // #63: `--json-body -` reads the body from stdin. Use --dry-run so nothing is created;
        // the dry-run preview proves the piped body was parsed and would be sent. Includes a
        // non-ASCII name to guard the UTF-8 stdin decoding (H1).
        const string name = "Søg 日本";
        var body = $$"""{"contentType":{"alias":"textPage"},"variants":[{"name":"{{name}}"}]}""";
        var result = CliRunner.RunWithInput(
            body,
            "content",
            "create",
            "--json-body",
            "-",
            "--dry-run"
        );

        Assert.True(result.Ok, result.Stderr);
        using var doc = JsonDocument.Parse(result.Stdout);
        var request = doc.RootElement.GetProperty("request");
        Assert.Equal("dry-run", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("POST", request.GetProperty("method").GetString());
        // The non-ASCII name must survive the pipe intact (UTF-8, no mojibake).
        var echoedName = request
            .GetProperty("body")
            .GetProperty("variants")[0]
            .GetProperty("name")
            .GetString();
        Assert.Equal(name, echoedName);
    }

    [SkippableFact]
    public void AuthProfiles_ListsProfilesWithDefault()
    {
        RequireLive();
        // #64: profiles are listed with the default marked. The live config resolves to at
        // least one profile (a legacy flat config migrates to 'default').
        var result = CliRunner.Run("auth", "profiles");
        Assert.True(result.Ok, result.Stderr);

        var rows = result.Data().EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r.GetProperty("default").GetString() == "*");
    }

    [SkippableFact]
    public void Profile_SelectsCredentials()
    {
        RequireLive();
        // Discover the default profile name, then prove --profile <name> authenticates with it.
        var profiles = CliRunner.Run("auth", "profiles");
        Assert.True(profiles.Ok, profiles.Stderr);
        var defaultName = profiles
            .Data()
            .EnumerateArray()
            .First(r => r.GetProperty("default").GetString() == "*")
            .GetProperty("profile")
            .GetString()!;

        var whoami = CliRunner.Run("auth", "whoami", "--profile", defaultName);
        Assert.True(whoami.Ok, whoami.Stderr);
    }

    [Fact]
    public void Commands_EmitsCatalogJson()
    {
        // #60: `commands` is a local introspection command - no instance or auth needed - so
        // this runs unconditionally and also verifies the end-to-end wiring (root captured).
        var result = CliRunner.Run("commands");

        Assert.True(result.Ok, result.Stderr);
        var catalog = result.Data();
        Assert.Equal(JsonValueKind.Object, catalog.ValueKind);
        var topLevel = catalog
            .GetProperty("commands")
            .EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("content", topLevel);
        Assert.Contains("commands", topLevel); // the catalog includes itself
        // Global options are described once, on the root.
        var rootOptions = catalog
            .GetProperty("options")
            .EnumerateArray()
            .Select(o => o.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("--output", rootOptions);
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
                .Any(w => w.GetProperty("id").GetString() == id);
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
