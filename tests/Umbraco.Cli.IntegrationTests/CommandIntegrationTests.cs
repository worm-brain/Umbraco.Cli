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
            var delete = CliRunner.Run("webhooks", "delete", id!);
            Assert.True(delete.Ok, delete.Stderr);
        }
    }
}
