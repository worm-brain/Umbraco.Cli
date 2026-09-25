using System.CommandLine;
using System.Text.Json;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Parse errors respect the requested output format (#167).
/// <para>
/// System.CommandLine's own handling writes plain text to stderr and the help screen to stdout
/// whatever was asked for, so piping a mistyped command into <c>jq</c> failed on the help text
/// rather than reading an error envelope. "JSON output is always parseable" is the contract the
/// rest of the CLI keeps, and a typo is the most likely moment to need it.
/// </para>
/// </summary>
[Collection("ConsoleCapture")]
public class ParseErrorReporterTests
{
    /// <summary>A root command with the global options attached, mirroring Program.cs.</summary>
    /// <returns>The root command and its global options.</returns>
    private static (RootCommand Root, GlobalOptions Options) BuildRoot()
    {
        var options = new GlobalOptions();
        var root = new RootCommand("test");
        options.AddTo(root);
        var noun = new Command("content", "content");
        var get = new Command("get", "get");
        get.Add(new Argument<Guid>("id"));
        noun.Add(get);
        root.Add(noun);
        return (root, options);
    }

    /// <summary>Captures stderr while <paramref name="action"/> runs.</summary>
    /// <param name="action">The action to run.</param>
    /// <returns>What it wrote to stderr.</returns>
    private static string CaptureErr(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }
        return writer.ToString();
    }

    [Fact]
    public void Report_WithJsonRequested_EmitsAParseableErrorEnvelope()
    {
        var (root, options) = BuildRoot();
        var parsed = root.Parse("content get notAGuid --output json");
        Assert.NotEmpty(parsed.Errors);

        var stderr = CaptureErr(() => ParseErrorReporter.Report(parsed, options));

        // The whole point: this parses. Before, stderr was prose and stdout was 27 lines of help.
        var root2 = JsonDocument.Parse(stderr).RootElement;
        Assert.Equal("error", root2.GetProperty("status").GetString());
        Assert.Equal(1, root2.GetProperty("exitCode").GetInt32());
        Assert.Contains("notAGuid", root2.GetProperty("message").GetString());
    }

    [Fact]
    public void Report_UnparseableValue_CategorisesAsInvalidArgument()
    {
        // #203: API errors carry a category; a usage error had none, so agents had to parse
        // the message to tell "my command was wrong" from a server fault.
        var (root, options) = BuildRoot();
        var parsed = root.Parse("content get notAGuid --output json");

        var stderr = CaptureErr(() => ParseErrorReporter.Report(parsed, options));

        var envelope = JsonDocument.Parse(stderr).RootElement;
        Assert.Equal("invalid_argument", envelope.GetProperty("category").GetString());
    }

    [Fact]
    public void Report_MessageSaysHowToGetUsage()
    {
        var (root, options) = BuildRoot();
        var parsed = root.Parse("content get notAGuid --output json");

        var stderr = CaptureErr(() => ParseErrorReporter.Report(parsed, options));

        // The help text is no longer dumped to stdout, so the envelope has to point at it.
        Assert.Contains(
            "--help",
            JsonDocument.Parse(stderr).RootElement.GetProperty("message").GetString()
        );
    }

    [Fact]
    public void Report_ReturnsExitCodeOne()
    {
        var (root, options) = BuildRoot();
        var parsed = root.Parse("content get notAGuid --output json");

        var code = 0;
        CaptureErr(() => code = ParseErrorReporter.Report(parsed, options));

        Assert.Equal(1, code);
    }

    [Theory]
    [InlineData("content get --help")]
    [InlineData("--version")]
    public void IsHelpOrVersion_LetsThoseThrough(string args)
    {
        var (root, _) = BuildRoot();

        // Help and version carry parse "errors" for the arguments they skipped; the user asked
        // for help and should get System.CommandLine's, not an error envelope.
        Assert.True(ParseErrorReporter.IsHelpOrVersion(root.Parse(args)));
    }

    [Fact]
    public void IsHelpOrVersion_IsFalseForARealMistake()
    {
        var (root, _) = BuildRoot();

        Assert.False(ParseErrorReporter.IsHelpOrVersion(root.Parse("content get notAGuid")));
    }
}
