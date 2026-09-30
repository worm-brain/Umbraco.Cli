using System.CommandLine;
using System.Text;
using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Dictionary;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;
using Umbraco.Cli.Infrastructure.Http;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>--value-file isoCode=path</c> on <c>dictionary create</c> and <c>update</c>: a translation read
/// from a file exactly as the file holds it, mixed freely with <c>--value</c> for other ISO codes,
/// while two sources for one ISO code, or two reads of stdin, are refused before any request.
/// </summary>
[Collection("ConsoleCapture")]
public class DictionaryValueFileTests : IDisposable
{
    private const string Auth = "--host https://x --token t --output json";

    /// <summary>A multi-line Markdown value, the case the option exists for, ending in a newline.</summary>
    private const string Markdown = "# Intro\n\nWelcome to the *blog*.\n";

    private static readonly Guid ItemId = Guid.Parse("3f7a8b2e-1234-5678-abcd-ef0123456789");

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        $"umbraco-value-file-{Guid.NewGuid():N}"
    );

    public DictionaryValueFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeClientFactory(IUmbracoManagementClient client)
        : IUmbracoManagementClientFactory
    {
        public IUmbracoManagementClient Create(HttpClient http) => client;
    }

    /// <summary>Runs a command line against the fake, capturing both streams.</summary>
    /// <param name="client">The fake client the command talks to.</param>
    /// <param name="args">The command line after the global options.</param>
    /// <returns>The exit code and what was written to stderr.</returns>
    private static async Task<(int Exit, string Stderr)> Run(
        IUmbracoManagementClient client,
        string args
    )
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
        var executor = new CommandExecutor(factory, new ConsoleConfirmationPrompt());
        var root = new RootCommand();
        global.AddTo(root);
        root.Add(DictionaryCommand.Build(executor));

        var (out_, err) = (Console.Out, Console.Error);
        var stderr = new StringWriter();
        Console.SetOut(new StringWriter());
        Console.SetError(stderr);
        try
        {
            var exit = await root.Parse($"{Auth} {args}").InvokeAsync();
            return (exit, stderr.ToString());
        }
        finally
        {
            Console.SetOut(out_);
            Console.SetError(err);
        }
    }

    /// <summary>Writes <paramref name="text"/> to a file in this test's folder.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="text">The file's text, written as UTF-8 without a byte-order mark.</param>
    /// <returns>The file's full path.</returns>
    private string WriteFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The translations a request carried, by ISO code.</summary>
    /// <param name="translations">The request's translations.</param>
    /// <returns>ISO code to translation text.</returns>
    private static Dictionary<string, string> ByIsoCode(
        IEnumerable<DictionaryTranslation> translations
    ) => translations.ToDictionary(t => t.IsoCode, t => t.Translation);

    [Fact]
    public async Task Update_ValueFileBesideValue_SendsBothTranslations()
    {
        // Arrange: the issue's example, with the item named by its key.
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DictionaryItem, "Blog.Intro")] = ItemId;
        var intro = WriteFile("intro.md", Markdown);

        // Act
        var (exit, _) = await Run(
            fake,
            $"dictionary update Blog.Intro --value-file en-US={intro} --value da-DK=Hej"
        );

        // Assert
        Assert.Equal(0, exit);
        var (id, request) = Assert.Single(fake.DictionaryItemsUpdated);
        Assert.Equal(ItemId, id);
        Assert.Equal(
            new Dictionary<string, string> { ["en-US"] = Markdown, ["da-DK"] = "Hej" },
            ByIsoCode(request.Translations)
        );
    }

    [Fact]
    public async Task Create_ValueFile_StoresTheFileTextVerbatim()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var intro = WriteFile("intro.md", Markdown);

        // Act
        var (exit, _) = await Run(
            fake,
            $"dictionary create --key Blog.Intro --value-file en-US={intro}"
        );

        // Assert
        Assert.Equal(0, exit);
        Assert.Equal(
            new Dictionary<string, string> { ["en-US"] = Markdown },
            ByIsoCode(Assert.Single(fake.DictionaryItemsCreated).Translations)
        );
    }

    [Fact]
    public async Task Create_ValueFileWithByteOrderMark_ReadsUtf8WithoutTheMark()
    {
        // Arrange: an editor on Windows may save UTF-8 with a BOM; it is not part of the text.
        var fake = new FakeUmbracoManagementClient();
        var path = Path.Combine(_dir, "search.da.txt");
        File.WriteAllText(path, "Søg", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        await Run(fake, $"dictionary create --key Common.Search --value-file da-DK={path}");

        // Assert
        Assert.Equal(
            "Søg",
            Assert.Single(Assert.Single(fake.DictionaryItemsCreated).Translations).Translation
        );
    }

    [Fact]
    public async Task Update_ValueFileMissing_IsAnInvalidArgumentErrorAndSendsNothing()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var missing = Path.Combine(_dir, "nope.md");

        // Act
        var (exit, stderr) = await Run(
            fake,
            $"dictionary update {ItemId} --value-file en-US={missing}"
        );

        // Assert
        using var error = JsonDocument.Parse(stderr);
        Assert.Equal(
            (1, "invalid_argument", 0),
            (
                exit,
                error.RootElement.GetProperty("category").GetString(),
                fake.DictionaryItemsUpdated.Count
            )
        );
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-us")] // ISO codes match ignoring case, as Umbraco merges them
    public async Task Update_SameIsoCodeInBothOptions_IsRefusedBeforeTheClient(string inlineIsoCode)
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var intro = WriteFile("intro.md", Markdown);

        // Act
        var (exit, _) = await Run(
            fake,
            $"dictionary update {ItemId} --value-file en-US={intro} --value {inlineIsoCode}=Hello"
        );

        // Assert
        Assert.Equal((1, 0), (exit, fake.DictionaryItemsUpdated.Count));
    }

    [Fact]
    public async Task Create_SameIsoCodeInTwoValueFiles_IsRefusedBeforeTheClient()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var a = WriteFile("a.md", "A");
        var b = WriteFile("b.md", "B");

        // Act
        var (exit, _) = await Run(
            fake,
            $"dictionary create --key Blog.Intro --value-file en-US={a} --value-file en-US={b}"
        );

        // Assert
        Assert.Equal((1, 0), (exit, fake.DictionaryItemsCreated.Count));
    }

    [Fact]
    public async Task Create_TwoStdinValueFiles_IsRefusedBeforeTheClient()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();

        // Act
        var (exit, _) = await Run(
            fake,
            "dictionary create --key Blog.Intro --value-file en-US=- --value-file da-DK=-"
        );

        // Assert
        Assert.Equal((1, 0), (exit, fake.DictionaryItemsCreated.Count));
    }

    [Fact]
    public async Task Update_ValueFileAlone_IsSomethingToUpdate()
    {
        // Arrange: the "nothing to update" check counts --value-file as a change.
        var fake = new FakeUmbracoManagementClient();
        var intro = WriteFile("intro.md", Markdown);

        // Act
        var (exit, _) = await Run(fake, $"dictionary update {ItemId} --value-file en-US={intro}");

        // Assert
        Assert.Equal((0, 1), (exit, fake.DictionaryItemsUpdated.Count));
    }

    // -- the parse-time checks, message by message --------------------------------

    [Fact]
    public void Problems_DifferentIsoCodes_FindsNothing()
    {
        var problems = DictionaryTranslationInput.Problems(["da-DK=Hej"], ["en-US=intro.md"]);

        Assert.Empty(problems);
    }

    [Fact]
    public void Problems_SameIsoCodeInBothOptions_NamesTheIsoCode()
    {
        var problem = Assert.Single(
            DictionaryTranslationInput.Problems(["en-US=Hello"], ["en-US=intro.md"])
        );

        Assert.Contains("Given more than once: en-US.", problem);
    }

    [Fact]
    public void Problems_TwoStdinPaths_NamesBothIsoCodes()
    {
        var problem = Assert.Single(
            DictionaryTranslationInput.Problems(null, ["en-US=-", "da-DK=-"])
        );

        Assert.Contains("Given for: en-US, da-DK.", problem);
    }

    [Fact]
    public void Problems_EmptyPath_PointsAtAnEmptyValue()
    {
        var problem = Assert.Single(DictionaryTranslationInput.Problems(null, ["en-US="]));

        Assert.Contains("--value en-US=", problem);
    }

    [Fact]
    public void Problems_MalformedToken_IsLeftToTheShapeCheck()
    {
        // "en-US" has no "=": KeyValuePairs.Validate reports it, so it is not reported twice.
        var problems = DictionaryTranslationInput.Problems(null, ["en-US"]);

        Assert.Empty(problems);
    }
}
