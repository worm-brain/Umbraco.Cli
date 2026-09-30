using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Diagnostics;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>meta.valueFormat</c> on the dictionary commands (#440): the site's declared format, <c>text</c>
/// when nothing declares one, absent when the manifests cannot be read, and never read for a call
/// that failed. Also <c>manifest list</c>'s view of the declarations.
/// </summary>
[Collection("ConsoleCapture")]
public class DictionaryValueFormatTests
{
    private const string Auth = "--host https://x --token t --output json";

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    private sealed class Prompt : IConfirmationPrompt
    {
        public bool IsInteractive => false;

        public bool Confirm(string message) => true;
    }

    /// <summary>A root with the dictionary and manifest nouns over <paramref name="client"/>.</summary>
    /// <param name="client">The fake client.</param>
    /// <returns>The root command.</returns>
    private static RootCommand BuildRoot(IUmbracoManagementClient client)
    {
        var stub = new StubHttpClientFactory();
        var global = new GlobalOptions();
        var factory = new CommandContextFactory(
            new ConfigStore(Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid()}.json")),
            new UmbracoAuthService(stub),
            stub,
            global,
            new FakeClientFactory(client),
            new MutationInterceptState()
        );
        var executor = new CommandExecutor(factory, new Prompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(DictionaryCommand.Build(executor));
        root.Add(ManifestCommand.Build(executor));
        return root;
    }

    /// <summary>Runs <paramref name="args"/> and captures both streams.</summary>
    /// <param name="client">The fake client.</param>
    /// <param name="args">The command line, without the auth options.</param>
    /// <returns>The exit code, stdout and stderr.</returns>
    private static async Task<(int Exit, string Out, string Err)> Run(
        FakeUmbracoManagementClient client,
        string args
    )
    {
        var (origOut, origErr) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await BuildRoot(client).Parse($"{Auth} {args}").InvokeAsync();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    /// <summary>A fake with one dictionary item, and manifests declaring <paramref name="formats"/>.</summary>
    /// <param name="id">The item's id.</param>
    /// <param name="formats">One declared format per package.</param>
    /// <returns>The fake client.</returns>
    private static FakeUmbracoManagementClient Site(Guid id, params string[] formats)
    {
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryItemsById[id] = new DictionaryItemResponse
        {
            Id = id,
            Name = "Blog.Intro",
            Translations =
            [
                new DictionaryTranslation { IsoCode = "en-US", Translation = "**Hi**" },
            ],
        };
        for (var i = 0; i < formats.Length; i++)
            fake.Manifests.Add(
                new ManifestResponse
                {
                    Id = $"Package{i}",
                    Name = $"Package {i}",
                    CliCapabilities = new JsonObject { ["dictionaryValueFormat"] = formats[i] },
                }
            );
        return fake;
    }

    /// <summary>The <c>meta.valueFormat</c> of a JSON envelope, or null when it is absent.</summary>
    /// <param name="stdout">The envelope.</param>
    /// <returns>The value, or null.</returns>
    private static string? ValueFormat(string stdout) =>
        JsonDocument
            .Parse(stdout)
            .RootElement.GetProperty("meta")
            .TryGetProperty("valueFormat", out var v)
            ? v.GetString()
            : null;

    [Fact]
    public async Task DictionaryGet_SiteDeclaresMarkdown_ReportsItInMeta()
    {
        // Arrange
        var id = Guid.NewGuid();
        var fake = Site(id, "markdown");

        // Act
        var (exit, stdout, _) = await Run(fake, $"dictionary get {id}");

        // Assert
        Assert.Equal((0, "markdown"), (exit, ValueFormat(stdout)));
    }

    [Fact]
    public async Task DictionaryGet_NothingDeclared_ReportsText()
    {
        // Arrange
        var id = Guid.NewGuid();
        var fake = Site(id);

        // Act
        var (exit, stdout, _) = await Run(fake, $"dictionary get {id}");

        // Assert
        Assert.Equal((0, "text"), (exit, ValueFormat(stdout)));
    }

    [Fact]
    public async Task DictionaryGet_ManifestsUnavailable_SucceedsWithoutValueFormat()
    {
        // Arrange
        var id = Guid.NewGuid();
        var fake = Site(id, "markdown");
        fake.ManifestsUnavailable = "forbidden";

        // Act
        var (exit, stdout, _) = await Run(fake, $"dictionary get {id}");

        // Assert
        Assert.Equal((0, null), (exit, ValueFormat(stdout)));
    }

    [Fact]
    public async Task DictionaryGet_PackagesDisagree_WarnsOnStderrAndReportsText()
    {
        // Arrange
        var id = Guid.NewGuid();
        var fake = Site(id, "html", "markdown");

        // Act
        var (exit, stdout, stderr) = await Run(fake, $"dictionary get {id}");

        // Assert
        Assert.Equal(
            (0, "text", true),
            (
                exit,
                ValueFormat(stdout),
                stderr.StartsWith("warning: packages disagree", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task DictionaryGet_ItemNotFound_DoesNotReadCapabilities()
    {
        // Arrange: no item with this id, so the get fails.
        var fake = Site(Guid.NewGuid(), "markdown");

        // Act
        var (exit, _, _) = await Run(fake, $"dictionary get {Guid.NewGuid()}");

        // Assert
        Assert.Equal((1, 0), (exit, fake.SiteCapabilityReads));
    }

    [Fact]
    public async Task DictionaryList_SiteDeclaresHtml_ReportsItInMeta()
    {
        // Arrange
        var fake = Site(Guid.NewGuid(), "html");

        // Act
        var (exit, stdout, _) = await Run(fake, "dictionary list");

        // Assert
        Assert.Equal((0, "html"), (exit, ValueFormat(stdout)));
    }

    [Fact]
    public async Task ManifestList_PackageDeclaresCliSupport_IncludesCliCapabilities()
    {
        // Arrange
        var fake = Site(Guid.NewGuid(), "markdown");

        // Act
        var (_, stdout, _) = await Run(fake, "manifest list");

        // Assert
        var manifest = Assert.Single(
            JsonDocument.Parse(stdout).RootElement.GetProperty("data").EnumerateArray()
        );
        Assert.Equal(
            "markdown",
            manifest.GetProperty("cliCapabilities").GetProperty("dictionaryValueFormat").GetString()
        );
    }

    [Fact]
    public async Task ManifestList_DoesNotAddValueFormat()
    {
        // Arrange: only the dictionary commands declare the field.
        var fake = Site(Guid.NewGuid(), "markdown");

        // Act
        var (_, stdout, _) = await Run(fake, "manifest list");

        // Assert
        Assert.Null(ValueFormat(stdout));
    }

    [Fact]
    public void DescribeCli_StringAndNonStringValues_ListsKeyValuePairs()
    {
        // Arrange
        var manifest = new ManifestResponse
        {
            CliCapabilities = new JsonObject { ["dictionaryValueFormat"] = "html", ["n"] = 2 },
        };

        // Act
        var cell = ManifestCommand.DescribeCli(manifest);

        // Assert
        Assert.Equal("dictionaryValueFormat=html, n=2", cell);
    }

    [Fact]
    public void DescribeCli_NothingDeclared_IsEmpty()
    {
        // Act
        var cell = ManifestCommand.DescribeCli(new ManifestResponse());

        // Assert
        Assert.Equal("", cell);
    }
}
