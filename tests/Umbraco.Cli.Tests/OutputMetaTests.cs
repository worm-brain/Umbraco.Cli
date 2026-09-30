using System.Text.Json;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="IOutputWriter.AddMeta"/> (#440): a command's own meta fields join every success
/// envelope's <c>meta</c>, a null value leaves the field out, errors never carry them, and
/// <c>--quiet</c> forwards them to the writer it wraps.
/// </summary>
[Collection("ConsoleCapture")]
public class OutputMetaTests
{
    /// <summary>Runs <paramref name="write"/> and returns what it printed to stdout and stderr.</summary>
    /// <param name="write">The writes to make.</param>
    /// <returns>The two streams.</returns>
    private static (string Out, string Err) Capture(Action write)
    {
        var (origOut, origErr) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            write();
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
        return (stdout.ToString(), stderr.ToString());
    }

    /// <summary>The envelope's <c>meta</c> object.</summary>
    /// <param name="json">The envelope.</param>
    /// <returns>Its meta.</returns>
    private static JsonElement Meta(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("meta");

    [Fact]
    public void WriteSuccess_AfterAddMeta_MetaCarriesTheField()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        writer.AddMeta("valueFormat", "markdown");

        // Act
        var (stdout, _) = Capture(() => writer.WriteSuccess(new { id = 1 }, "dictionary.get"));

        // Assert
        Assert.Equal("markdown", Meta(stdout).GetProperty("valueFormat").GetString());
    }

    [Fact]
    public void WriteSuccess_AfterAddMeta_KeepsTheStandardFields()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        writer.AddMeta("valueFormat", "markdown");

        // Act
        var (stdout, _) = Capture(() => writer.WriteSuccess(new { id = 1 }, "dictionary.get", 5));

        // Assert
        Assert.Equal(
            ["command", "durationMs", "timestamp", "schemaVersion", "valueFormat"],
            Meta(stdout).EnumerateObject().Select(p => p.Name)
        );
    }

    [Fact]
    public void WriteList_AfterAddMeta_MetaCarriesTheField()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        writer.AddMeta("valueFormat", "html");

        // Act
        var (stdout, _) = Capture(() =>
            writer.WriteList([], ["ID"], [], ListPaging.Complete(0), "dictionary.list")
        );

        // Assert
        Assert.Equal("html", Meta(stdout).GetProperty("valueFormat").GetString());
    }

    [Fact]
    public void AddMeta_NullValue_LeavesTheFieldOut()
    {
        // Arrange: set, then cleared.
        var writer = new JsonOutputWriter();
        writer.AddMeta("valueFormat", "html");
        writer.AddMeta("valueFormat", null);

        // Act
        var (stdout, _) = Capture(() => writer.WriteSuccess(new { id = 1 }, "dictionary.get"));

        // Assert
        Assert.False(Meta(stdout).TryGetProperty("valueFormat", out _));
    }

    [Fact]
    public void WriteError_AfterAddMeta_DoesNotCarryTheField()
    {
        // Arrange
        var writer = new JsonOutputWriter();
        writer.AddMeta("valueFormat", "html");

        // Act
        var (_, stderr) = Capture(() =>
            writer.WriteError(
                ExitCode.Failed,
                FailureCategory.RequestRejected,
                "gone",
                "dictionary.get"
            )
        );

        // Assert
        Assert.False(Meta(stderr).TryGetProperty("valueFormat", out _));
    }

    [Fact]
    public void QuietOutputWriter_AddMeta_ForwardsToTheInnerWriter()
    {
        // Arrange: a read under --quiet still prints its data, so its meta must carry the field.
        var inner = new JsonOutputWriter();
        var quiet = new QuietOutputWriter(inner, isWrite: false);

        // Act
        quiet.AddMeta("valueFormat", "text");
        var (stdout, _) = Capture(() => quiet.WriteSuccess(new { id = 1 }, "dictionary.get"));

        // Assert
        Assert.Equal("text", Meta(stdout).GetProperty("valueFormat").GetString());
    }
}
