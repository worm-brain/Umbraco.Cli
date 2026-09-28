using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

// Console.SetOut/SetError are process-global, so we serialise these tests.
[Collection("ConsoleCapture")]
public class JsonOutputWriterTests
{
    private readonly JsonOutputWriter _writer = new();

    [Fact]
    public void WriteError_QuotesAndNonAsciiLetters_AreWrittenAsThemselves()
    {
        // #311: the default encoder wrote ' as ' and æ as æ, so messages were unreadable.
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Failed,
                FailureCategory.InvalidArgument,
                "'Søren' is not \"valid\" + more",
                "content.get"
            )
        );

        Assert.Contains("'Søren' is not \\\"valid\\\" + more", stderr);
    }

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
        Assert.Equal("6", version);
    }

    [Fact]
    public void WriteMessage_CarriesSchemaVersion()
    {
        // #61: message-shaped success envelopes (delete/publish) are versioned too.
        var (stdout, _) = Capture(() => _writer.WriteMessage(new { id = "1" }, "Done."));
        var meta = JsonDocument.Parse(stdout).RootElement.GetProperty("meta");
        Assert.Equal("6", meta.GetProperty("schemaVersion").GetString());
    }

    // ── meta parity across output shapes (#137) ────────────────────────────────

    [Fact]
    public void WriteTable_MetaContainsCommandAndDuration()
    {
        // #137: list/table output must carry meta.command and meta.durationMs like object output.
        var (stdout, _) = Capture(() =>
            _writer.WriteTable(
                ["id", "name"],
                [
                    ["1", "a"],
                ],
                "content.list",
                99
            )
        );
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("data").ValueKind);
        var meta = doc.RootElement.GetProperty("meta");
        Assert.Equal("content.list", meta.GetProperty("command").GetString());
        Assert.Equal(99, meta.GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public void WriteMessage_MetaContainsCommandAndDuration()
    {
        // #137: message-shaped success (delete/publish) carries command/duration/timestamp too.
        var (stdout, _) = Capture(() =>
            _writer.WriteMessage(new { id = "1" }, "Deleted.", "content.delete", 12)
        );
        var meta = JsonDocument.Parse(stdout).RootElement.GetProperty("meta");
        Assert.Equal("content.delete", meta.GetProperty("command").GetString());
        Assert.Equal(12, meta.GetProperty("durationMs").GetInt64());
        Assert.True(DateTimeOffset.TryParse(meta.GetProperty("timestamp").GetString(), out _));
    }

    [Fact]
    public void WriteTable_MetaKeysMatchWriteSuccess()
    {
        // #137: an agent keying on meta.command cannot rely on it unless list (table) and object
        // (WriteSuccess) envelopes expose the same meta keys. Assert the sets are identical.
        var (tableOut, _) = Capture(() =>
            _writer.WriteTable(
                ["id", "name"],
                [
                    ["1", "a"],
                ],
                "content.list",
                5
            )
        );
        var (objectOut, _) = Capture(() => _writer.WriteSuccess(new { id = 1 }, "content.get", 5));

        var tableKeys = JsonDocument
            .Parse(tableOut)
            .RootElement.GetProperty("meta")
            .EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n);
        var objectKeys = JsonDocument
            .Parse(objectOut)
            .RootElement.GetProperty("meta")
            .EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(n => n);
        Assert.Equal(objectKeys, tableKeys);
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

    [Fact]
    public void WriteSuccess_WithFields_UnknownField_IsAbsentNotError()
    {
        var writer = new JsonOutputWriter(["id", "nope"]);
        var (stdout, _) = Capture(() => writer.WriteSuccess(new { id = "1", name = "A" }));
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("id", out _));
        Assert.False(data.TryGetProperty("nope", out _));
        Assert.Equal(1, data.EnumerateObject().Count());
    }

    [Fact]
    public void WriteSuccess_WithFields_ScalarData_PassesThrough()
    {
        // Projection only makes sense for objects; a scalar payload is emitted unchanged.
        var writer = new JsonOutputWriter(["id"]);
        var (stdout, _) = Capture(() => writer.WriteSuccess(42));
        var data = JsonDocument.Parse(stdout).RootElement.GetProperty("data");
        Assert.Equal(42, data.GetInt32());
    }

    [Fact]
    public void WriteSuccess_WithFields_MatchesIgnoringSpaces()
    {
        // A table's "Content Type" header is emitted as the camelCase key "contentType" (#87)
        // and is selectable via --fields contentType.
        var writer = new JsonOutputWriter(["contentType"]);
        var (stdout, _) = Capture(() =>
            writer.WriteTable(
                ["Content Type", "Name"],
                [
                    ["textPage", "About"],
                ]
            )
        );
        var item = JsonDocument.Parse(stdout).RootElement.GetProperty("data")[0];
        Assert.Equal("textPage", item.GetProperty("contentType").GetString());
        Assert.False(item.TryGetProperty("name", out _));
    }

    // ── WriteError ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteError_StatusIsError()
    {
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Failed,
                FailureCategory.RequestRejected,
                "Not found",
                "content.get",
                404
            )
        );
        var doc = JsonDocument.Parse(stderr);
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WriteError_SeparatesTheExitCodeFromTheHttpStatus()
    {
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Failed,
                FailureCategory.RequestRejected,
                "Unauthorized",
                "content.get",
                401
            )
        );
        var doc = JsonDocument.Parse(stderr);

        // #177: `code` used to carry whichever of the two applied, so a caller could not act on
        // it without already knowing which kind of failure it had.
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(401, doc.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("Unauthorized", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void WriteError_PolicyFailure_OmitsTheHttpStatus()
    {
        // A failure that never reached the server has no status to report - absent, not 0.
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Aborted,
                FailureCategory.NotAuthenticated,
                "Not authenticated",
                "content.list"
            )
        );
        var root = JsonDocument.Parse(stderr).RootElement;

        Assert.Equal(2, root.GetProperty("exitCode").GetInt32());
        Assert.False(root.TryGetProperty("httpStatus", out _));
    }

    [Fact]
    public void WriteError_CarriesSchemaVersionInMetaLikeSuccess()
    {
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Aborted,
                FailureCategory.NotAuthenticated,
                "Not authenticated",
                "content.list"
            )
        );
        var root = JsonDocument.Parse(stderr).RootElement;

        // #177: it used to sit at the top level, so the error envelope was the one shape that did
        // not carry a meta object at all.
        Assert.Equal("6", root.GetProperty("meta").GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void WriteError_WritesToStderr_NotStdout()
    {
        var (stdout, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Failed,
                FailureCategory.ServerError,
                "oops",
                "content.get",
                500
            )
        );
        Assert.Empty(stdout);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public void WriteError_WithCategoryAndServerVersion_EmitsBothFields()
    {
        // #152: an API failure carries a machine-readable category and the connected server
        // version so a caller can attribute the failure without a controlled experiment.
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Failed,
                FailureCategory.ServerError,
                "oops",
                "content.get",
                500,
                "17.3.5"
            )
        );
        var root = JsonDocument.Parse(stderr).RootElement;
        Assert.Equal("server_error", root.GetProperty("category").GetString());
        Assert.Equal("17.3.5", root.GetProperty("serverVersion").GetString());
    }

    [Fact]
    public void WriteError_PolicyFailure_CarriesItsCategoryButNoServerVersion()
    {
        // docs/conventions.md section 7: every error has a category, policy aborts included;
        // the server version only belongs to failures the server answered.
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Aborted,
                FailureCategory.Cancelled,
                "Operation cancelled.",
                "content.delete"
            )
        );
        var root = JsonDocument.Parse(stderr).RootElement;

        Assert.Equal("cancelled", root.GetProperty("category").GetString());
        Assert.False(root.TryGetProperty("serverVersion", out _));
    }

    [Fact]
    public void WriteError_CarriesTheFullMeta()
    {
        // The error meta matches a success's: the command and a timestamp, not just the version.
        var (_, stderr) = Capture(() =>
            _writer.WriteError(
                ExitCode.Aborted,
                FailureCategory.NotAllowed,
                "Not in the allow-list.",
                "content.delete"
            )
        );
        var meta = JsonDocument.Parse(stderr).RootElement.GetProperty("meta");

        Assert.Equal("content.delete", meta.GetProperty("command").GetString());
        Assert.True(meta.TryGetProperty("timestamp", out _), "meta.timestamp is missing");
    }

    // ── WriteTable ───────────────────────────────────────────────────────────

    [Fact]
    public void WriteTable_ProducesArrayOfObjectsKeyedByCamelCaseHeader()
    {
        // #87: keys are camelCased header names ("ID" -> "id") so list output agrees with get.
        var headers = new[] { "ID", "Name" };
        var rows = new[] { new[] { "1", "Alice" }, new[] { "2", "Bob" } };

        var (stdout, _) = Capture(() => _writer.WriteTable(headers, rows));
        var doc = JsonDocument.Parse(stdout);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("Alice", data[0].GetProperty("name").GetString());
        Assert.Equal("2", data[1].GetProperty("id").GetString());
        // The human header text is no longer used as a key.
        Assert.False(data[0].TryGetProperty("ID", out _));
    }

    [Fact]
    public void WriteTable_CollidingHeaderKeys_ThrowsRatherThanDroppingColumn()
    {
        // #87 review fix: two headers whose camelCase keys collide would silently overwrite a
        // column; the writer must fail loudly instead.
        Assert.Throws<InvalidOperationException>(() =>
            Capture(() =>
                _writer.WriteTable(
                    ["ID", "Id"],
                    [
                        ["1", "2"],
                    ]
                )
            )
        );
    }

    [Fact]
    public void WriteTable_MultiWordHeader_BecomesCamelCaseKey()
    {
        // #87: "Content Type" -> "contentType", "Version ID" -> "versionId".
        var (stdout, _) = Capture(() =>
            _writer.WriteTable(
                ["Content Type", "Version ID"],
                [
                    ["textPage", "7"],
                ]
            )
        );
        var item = JsonDocument.Parse(stdout).RootElement.GetProperty("data")[0];
        Assert.Equal("textPage", item.GetProperty("contentType").GetString());
        Assert.Equal("7", item.GetProperty("versionId").GetString());
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
        var (stdout, _) = Capture(() => _writer.WriteMessage(new { id = "1" }, "Done."));
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("success", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WriteMessage_CarriesTheDataAndLeavesTheMessageToHumans()
    {
        // docs/conventions.md 6.2: every success has data; the message is human output only.
        var (stdout, _) = Capture(() =>
            _writer.WriteMessage(new { id = "3f7a8b2e" }, "Item deleted.")
        );
        var root = JsonDocument.Parse(stdout).RootElement;

        Assert.Equal(
            ("3f7a8b2e", false),
            (
                root.GetProperty("data").GetProperty("id").GetString(),
                root.TryGetProperty("message", out _)
            )
        );
    }
}
