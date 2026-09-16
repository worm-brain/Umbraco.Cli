using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

public class OutputFormatTests
{
    // ── OutputFormatParser ────────────────────────────────────────────────────

    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("JSON", OutputFormat.Json)]
    [InlineData("Json", OutputFormat.Json)]
    [InlineData("human", OutputFormat.Human)]
    [InlineData("HUMAN", OutputFormat.Human)]
    [InlineData("csv", OutputFormat.Csv)]
    [InlineData("CSV", OutputFormat.Csv)]
    public void Parse_KnownValues_ReturnCorrectFormat(string input, OutputFormat expected)
    {
        var result = OutputFormatParser.Parse(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("table")]
    [InlineData("xml")]
    public void Parse_UnknownOrNull_ReturnsNull(string? input)
    {
        var result = OutputFormatParser.Parse(input);
        Assert.Null(result);
    }

    // ── OutputWriterFactory ──────────────────────────────────────────────────

    [Fact]
    public void OutputWriterFactory_JsonRequested_ReturnsJsonWriter()
    {
        var writer = OutputWriterFactory.Create(OutputFormat.Json);
        Assert.IsType<JsonOutputWriter>(writer);
    }

    [Fact]
    public void OutputWriterFactory_HumanRequested_ReturnsHumanWriter()
    {
        var writer = OutputWriterFactory.Create(OutputFormat.Human);
        Assert.IsType<HumanOutputWriter>(writer);
    }

    [Fact]
    public void OutputWriterFactory_CsvRequested_ReturnsCsvWriter()
    {
        var writer = OutputWriterFactory.Create(OutputFormat.Csv);
        Assert.IsType<CsvOutputWriter>(writer);
    }

    [Fact]
    public void OutputWriterFactory_NullRequested_ReturnsWriterBasedOnTty()
    {
        // In a test runner stdout is redirected → should default to JSON.
        var writer = OutputWriterFactory.Create(null);
        Assert.IsType<JsonOutputWriter>(writer);
    }

    // ── NO_COLOR (#94) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)] // presence-based per no-color.org: any value (incl. empty) disables colour
    [InlineData("1", true)]
    [InlineData("false", true)] // even "false" counts as present
    public void NoColorRequested_IsPresenceBased(string? value, bool expected)
    {
        Assert.Equal(expected, ConsoleColorSetup.NoColorRequested(value));
    }
}
