using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the CSV output writer (#93): tables map directly to CSV, object/scalar success
/// values flatten to a header + value row, fields are RFC-4180 escaped, and errors go to stderr.
/// </summary>
[Collection("ConsoleCapture")]
public class CsvOutputWriterTests
{
    private readonly CsvOutputWriter _writer = new();

    private static (string stdout, string stderr) Capture(Action action)
    {
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(outSw);
        Console.SetError(errSw);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
        return (outSw.ToString(), errSw.ToString());
    }

    [Fact]
    public void WriteTable_EmitsHeaderAndRows()
    {
        var (stdout, _) = Capture(() =>
            _writer.WriteTable(
                ["Id", "Name"],
                [
                    ["1", "Home"],
                    ["2", "About"],
                ]
            )
        );

        var lines = stdout.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("Id,Name", lines[0]);
        Assert.Equal("1,Home", lines[1]);
        Assert.Equal("2,About", lines[2]);
    }

    [Fact]
    public void WriteTable_EscapesCommasAndQuotes()
    {
        var (stdout, _) = Capture(() =>
            _writer.WriteTable(
                ["Name"],
                [
                    ["Doe, John"],
                    ["a \"quoted\" b"],
                ]
            )
        );

        var lines = stdout.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("\"Doe, John\"", lines[1]);
        Assert.Equal("\"a \"\"quoted\"\" b\"", lines[2]);
    }

    [Fact]
    public void WriteSuccess_Object_FlattensToHeaderAndValueRow()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess(new { id = 1, name = "Home" }));

        var lines = stdout.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("id,name", lines[0]);
        Assert.Equal("1,Home", lines[1]);
    }

    [Fact]
    public void WriteSuccess_Scalar_EmitsBareValue()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess(true));

        Assert.Equal("true", stdout.TrimEnd());
    }

    [Fact]
    public void WriteError_WritesCodeAndMessageToStderr()
    {
        var (stdout, stderr) = Capture(() => _writer.WriteError(404, "Not found"));

        Assert.Empty(stdout);
        var lines = stderr.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("code,message", lines[0]);
        Assert.Equal("404,Not found", lines[1]);
    }
}
