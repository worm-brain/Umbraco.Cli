using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

// Console.SetOut/SetError are process-global, so we serialise these tests.
[Collection("ConsoleCapture")]
public class JsonOutputWriterTests
{
    private readonly JsonOutputWriter _writer = new();

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

    // ── WriteSuccess ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteSuccess_StatusIsSuccess()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess(new { id = 1 }));
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WriteSuccess_MetaCarriesSchemaVersion()
    {
        // #61: the envelope is versioned so agents can gate on the contract.
        var (stdout, _) = Capture(() => _writer.WriteSuccess(new { id = 1 }));
        var doc = JsonDocument.Parse(stdout);
        var version = doc.RootElement.GetProperty("meta").GetProperty("schemaVersion").GetString();
        // Assert the literal so a deliberate contract bump is a deliberate test change.
        Assert.Equal("1", version);
    }

    [Fact]
    public void WriteMessage_CarriesSchemaVersion()
    {
        // #61: message-shaped success envelopes (delete/publish) are versioned too.
        var (stdout, _) = Capture(() => _writer.WriteMessage("Done."));
        var meta = JsonDocument.Parse(stdout).RootElement.GetProperty("meta");
        Assert.Equal("1", meta.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void WriteSuccess_DataIsPresent()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess(new { name = "test" }));
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("test", doc.RootElement.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public void WriteSuccess_MetaContainsCommandAndDuration()
    {
        var (stdout, _) = Capture(() =>
            _writer.WriteSuccess(42, commandName: "content.list", durationMs: 99)
        );
        var doc = JsonDocument.Parse(stdout);
        var meta = doc.RootElement.GetProperty("meta");
        Assert.Equal("content.list", meta.GetProperty("command").GetString());
        Assert.Equal(99, meta.GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public void WriteSuccess_MetaContainsTimestamp()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess("anything"));
        var doc = JsonDocument.Parse(stdout);
        var ts = doc.RootElement.GetProperty("meta").GetProperty("timestamp");
        Assert.Equal(JsonValueKind.String, ts.ValueKind);
        Assert.True(DateTimeOffset.TryParse(ts.GetString(), out _));
    }

    [Fact]
    public void WriteSuccess_NullCommandAndDuration_OmittedFromMeta()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess("anything"));
        var doc = JsonDocument.Parse(stdout);
        var meta = doc.RootElement.GetProperty("meta");
        Assert.False(meta.TryGetProperty("command", out _));
        Assert.False(meta.TryGetProperty("durationMs", out _));
    }

    [Fact]
    public void WriteSuccess_WritesToStdout_NotStderr()
    {
        var (stdout, stderr) = Capture(() => _writer.WriteSuccess(1));
        Assert.NotEmpty(stdout);
        Assert.Empty(stderr);
    }

    // ── Field projection (#63) ─────────────────────────────────────────────────

    [Fact]
    public void WriteSuccess_WithFields_KeepsOnlyListedFieldsInOrder()
    {
        var writer = new JsonOutputWriter(["name", "id"]);
        var (stdout, _) = Capture(() =>
            writer.WriteSuccess(
                new
                {
                    id = "1",
                    name = "A",
                    extra = "x",
                }
            )
        );
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.Equal(new[] { "name", "id" }, data.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void WriteSuccess_WithFields_ProjectsEachArrayItem()
    {
        var writer = new JsonOutputWriter(["id"]);
        var (stdout, _) = Capture(() =>
            writer.WriteSuccess(
                new[] { new { id = "1", name = "A" }, new { id = "2", name = "B" } }
            )
        );
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        foreach (var item in data.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("id", out _));
            Assert.False(item.TryGetProperty("name", out _));
        }
    }

    [Fact]
    public void WriteSuccess_WithFields_MatchesCaseInsensitively()
    {
        var writer = new JsonOutputWriter(["ID"]); // requested in a different case
        var (stdout, _) = Capture(() => writer.WriteSuccess(new { id = "1", name = "A" }));
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("id", out _));
        Assert.False(data.TryGetProperty("name", out _));
    }

    [Fact]
    public void WriteSuccess_NoFields_KeepsAllData()
    {
        var (stdout, _) = Capture(() => _writer.WriteSuccess(new { id = "1", name = "A" }));
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.Equal(2, data.EnumerateObject().Count());
    }

    // ── WriteError ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteError_StatusIsError()
    {
        var (_, stderr) = Capture(() => _writer.WriteError(404, "Not found"));
        var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WriteError_CodeAndMessagePresent()
    {
        var (_, stderr) = Capture(() => _writer.WriteError(401, "Unauthorized"));
        var doc = JsonDocument.Parse(stderr);
        Assert.Equal(401, doc.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("Unauthorized", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void WriteError_WritesToStderr_NotStdout()
    {
        var (stdout, stderr) = Capture(() => _writer.WriteError(500, "oops"));
        Assert.Empty(stdout);
        Assert.NotEmpty(stderr);
    }

    // ── WriteTable ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteTable_ProducesArrayOfObjectsKeyedByHeader()
    {
        var headers = new[] { "ID", "Name" };
        var rows = new[] { new[] { "1", "Alice" }, new[] { "2", "Bob" } };

        var (stdout, _) = Capture(() => _writer.WriteTable(headers, rows));
        var doc = JsonDocument.Parse(stdout);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("Alice", data[0].GetProperty("Name").GetString());
        Assert.Equal("2", data[1].GetProperty("ID").GetString());
    }

    [Fact]
    public void WriteTable_EmptyRows_ProducesEmptyArray()
    {
        var (stdout, _) = Capture(() => _writer.WriteTable(["ID"], []));
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetArrayLength());
    }

    // ── WriteMessage ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteMessage_StatusIsSuccess()
    {
        var (stdout, _) = Capture(() => _writer.WriteMessage("Done."));
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WriteMessage_MessageFieldPresent()
    {
        var (stdout, _) = Capture(() => _writer.WriteMessage("Item deleted."));
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("Item deleted.", doc.RootElement.GetProperty("message").GetString());
    }
}
