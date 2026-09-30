using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The dictionary value format in schema snapshots (#442): <c>schema export</c> records the site's
/// format, and <c>schema diff</c> / <c>schema apply</c> warn - never refuse - when a snapshot's
/// dictionary values would be written to a site that stores another format.
/// </summary>
[Collection("ConsoleCapture")]
public class SchemaValueFormatTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    private sealed class NonInteractivePrompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => false;
    }

    /// <summary>A change row for a dictionary item, as the diff engine makes one.</summary>
    /// <param name="name">The item's key.</param>
    /// <returns>The change.</returns>
    private static SchemaEntityChange Change(string name) =>
        new(SchemaKinds.DictionaryItem, SchemaChangeKind.Added, name, null, null);

    /// <summary>A diff whose dictionary section adds, changes and removes the given counts.</summary>
    /// <param name="added">Items created.</param>
    /// <param name="changed">Items updated.</param>
    /// <param name="removed">Items deleted.</param>
    /// <returns>The diff.</returns>
    private static SchemaDiff DictionaryDiff(int added, int changed, int removed) =>
        new(
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None
        )
        {
            DictionaryItems = new SchemaKindDiff(
                [.. Enumerable.Range(0, added).Select(i => Change($"Added{i}"))],
                [.. Enumerable.Range(0, changed).Select(i => Change($"Changed{i}"))],
                [.. Enumerable.Range(0, removed).Select(i => Change($"Removed{i}"))],
                [],
                0
            ),
        };

    /// <summary>A snapshot carrying only a dictionary value format.</summary>
    /// <param name="format">The format, or null.</param>
    /// <returns>The snapshot.</returns>
    private static SchemaSnapshot WithFormat(string? format) =>
        new() { DictionaryValueFormat = format };

    // ── SchemaPipeline.FormatMismatch ────────────────────────────────────────

    [Fact]
    public void FormatMismatch_DifferentFormatsAndItemsWritten_WarnsNamingBoth()
    {
        // Act
        var warning = SchemaPipeline.FormatMismatch(
            WithFormat("markdown"),
            WithFormat("text"),
            DictionaryDiff(added: 1, changed: 2, removed: 0)
        );

        // Assert
        Assert.Equal(
            "the snapshot's dictionary values are 'markdown' but this site stores 'text'; "
                + "3 dictionary item(s) would be written as they are, without conversion.",
            warning
        );
    }

    [Fact]
    public void FormatMismatch_SameFormat_IsNull()
    {
        // Act
        var warning = SchemaPipeline.FormatMismatch(
            WithFormat("html"),
            WithFormat("html"),
            DictionaryDiff(1, 0, 0)
        );

        // Assert
        Assert.Null(warning);
    }

    [Theory]
    [InlineData(null, "text")]
    [InlineData("markdown", null)]
    public void FormatMismatch_EitherFormatUnknown_IsNull(string? from, string? to)
    {
        // Act
        var warning = SchemaPipeline.FormatMismatch(
            WithFormat(from),
            WithFormat(to),
            DictionaryDiff(1, 0, 0)
        );

        // Assert
        Assert.Null(warning);
    }

    [Fact]
    public void FormatMismatch_OnlyDeletes_IsNull()
    {
        // Act: a delete writes no values, so the formats don't matter.
        var warning = SchemaPipeline.FormatMismatch(
            WithFormat("markdown"),
            WithFormat("text"),
            DictionaryDiff(0, 0, removed: 2)
        );

        // Assert
        Assert.Null(warning);
    }

    // ── The snapshot file ────────────────────────────────────────────────────

    [Fact]
    public void ToJson_FormatSet_WritesIt()
    {
        // Act
        var json = WithFormat("markdown").ToJson();

        // Assert
        Assert.Equal(
            "markdown",
            JsonNode.Parse(json)!["dictionaryValueFormat"]!.GetValue<string>()
        );
    }

    [Fact]
    public void ToJson_FormatUnknown_LeavesItOut()
    {
        // Act
        var json = WithFormat(null).ToJson();

        // Assert
        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("dictionaryValueFormat"));
    }

    [Fact]
    public void FromJson_SnapshotWithoutTheField_LoadsWithFormatUnknown()
    {
        // Act: a format "4" file written before #442.
        var snapshot = SchemaSnapshot.FromJson("""{"schemaVersion":"4","dictionaryItems":[]}""");

        // Assert
        Assert.Null(snapshot.DictionaryValueFormat);
    }

    [Fact]
    public void FromJson_SnapshotWithTheField_ReadsIt()
    {
        // Act
        var snapshot = SchemaSnapshot.FromJson(
            """{"schemaVersion":"4","dictionaryValueFormat":"html","dictionaryItems":[]}"""
        );

        // Assert
        Assert.Equal("html", snapshot.DictionaryValueFormat);
    }

    // ── schema export ────────────────────────────────────────────────────────

    /// <summary>A fake site whose one package declares <paramref name="format"/>.</summary>
    /// <param name="format">The declared format, or null for none.</param>
    /// <returns>The fake.</returns>
    private static FakeUmbracoManagementClient Site(string? format)
    {
        var fake = new FakeUmbracoManagementClient();
        if (format is not null)
            fake.Manifests.Add(
                new ManifestResponse
                {
                    Id = "Rich.Package",
                    CliCapabilities = new JsonObject { ["dictionaryValueFormat"] = format },
                }
            );
        return fake;
    }

    [Fact]
    public async Task ExportAsync_WithTheDictionary_RecordsTheSitesFormat()
    {
        // Arrange
        var fake = Site("markdown");
        SchemaKindSpec[] kinds = [SchemaKinds.All.Single(k => k.Tag == SchemaKinds.DictionaryItem)];

        // Act
        var result = await SchemaExporter.ExportAsync(fake, kinds, CancellationToken.None);

        // Assert
        Assert.Equal("markdown", result.Data!.DictionaryValueFormat);
    }

    [Fact]
    public async Task ExportAsync_WithoutTheDictionary_DoesNotReadTheManifest()
    {
        // Arrange
        var fake = Site("markdown");
        SchemaKindSpec[] kinds = [SchemaKinds.All.Single(k => k.Tag == SchemaKinds.Language)];

        // Act
        var result = await SchemaExporter.ExportAsync(fake, kinds, CancellationToken.None);

        // Assert
        Assert.Equal((null, 0), (result.Data!.DictionaryValueFormat, fake.SiteCapabilityReads));
    }

    // ── schema diff ──────────────────────────────────────────────────────────

    /// <summary>Runs <c>schema diff</c> on <paramref name="snapshot"/> against <paramref name="fake"/>.</summary>
    /// <param name="fake">The live site.</param>
    /// <param name="snapshot">The snapshot's JSON.</param>
    /// <returns>The exit code, stdout and stderr.</returns>
    private static async Task<(int Exit, string Out, string Err)> Diff(
        FakeUmbracoManagementClient fake,
        string snapshot
    )
    {
        var path = Path.Combine(Path.GetTempPath(), $"snap-{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(path, snapshot);
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")),
            new UmbracoAuthService(stub),
            stub,
            global,
            new FakeClientFactory(fake),
            new MutationInterceptState()
        );
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(SchemaCommand.Build(new CommandExecutor(factory, new NonInteractivePrompt())));

        var (origOut, origErr) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await root.Parse(
                    $"--host https://x --token t --output json schema diff {path}"
                )
                .InvokeAsync();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            File.Delete(path);
        }
    }

    /// <summary>
    /// A snapshot of one dictionary item the live site doesn't have, in <paramref name="format"/>.
    /// The id is fixed, so two diffs of it produce the same rows.
    /// </summary>
    /// <param name="format">The snapshot's recorded format.</param>
    /// <returns>The snapshot JSON.</returns>
    private static string OneNewItem(string format) =>
        $$"""
            {"schemaVersion":"4","dictionaryValueFormat":"{{format}}","dictionaryItems":[
              {"id":"0e0c3f4a-0000-4000-8000-000000000442","name":"Blog.Intro","translations":[{"isoCode":"en-US","translation":"**Hi**"}]}
            ]}
            """;

    [Fact]
    public async Task SchemaDiff_MarkdownSnapshotAgainstTextSite_WarnsOnStderr()
    {
        // Arrange: the live site declares nothing, so it stores text.
        var fake = Site(null);

        // Act
        var (exit, _, stderr) = await Diff(fake, OneNewItem("markdown"));

        // Assert
        Assert.Equal(
            (0, true),
            (
                exit,
                stderr.Contains(
                    "warning: the snapshot's dictionary values are 'markdown'",
                    StringComparison.Ordinal
                )
            )
        );
    }

    [Fact]
    public async Task SchemaDiff_MismatchWarning_LeavesTheDiffRowsUnchanged()
    {
        // Arrange: the same diff against a site that matches, and one that doesn't.
        var (_, matching, _) = await Diff(Site("markdown"), OneNewItem("markdown"));
        var (_, mismatched, _) = await Diff(Site(null), OneNewItem("markdown"));

        // Act
        string Rows(string envelope) =>
            JsonDocument.Parse(envelope).RootElement.GetProperty("data").GetRawText();

        // Assert
        Assert.Equal(Rows(matching), Rows(mismatched));
    }

    [Fact]
    public async Task SchemaDiff_SameFormat_DoesNotWarn()
    {
        // Act
        var (_, _, stderr) = await Diff(Site("markdown"), OneNewItem("markdown"));

        // Assert
        Assert.DoesNotContain("warning:", stderr, StringComparison.Ordinal);
    }
}
