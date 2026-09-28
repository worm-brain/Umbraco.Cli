using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of the quiet output decorator (#94): <c>WriteMessage</c> success chatter is suppressed
/// while data, errors, and dry-run previews pass through unchanged.
/// </summary>
public class QuietOutputWriterTests
{
    /// <summary>A recording writer that captures which methods were called.</summary>
    private sealed class RecordingWriter : IOutputWriter
    {
        public bool SuccessCalled { get; private set; }
        public bool ErrorCalled { get; private set; }
        public bool TableCalled { get; private set; }
        public bool MessageCalled { get; private set; }
        public bool DryRunCalled { get; private set; }
        public string? LastTableCommand { get; private set; }
        public long? LastTableDuration { get; private set; }
        public string? LastErrorCategory { get; private set; }
        public string? LastErrorServerVersion { get; private set; }

        public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null) =>
            SuccessCalled = true;

        public int? LastExitCode { get; private set; }
        public int? LastHttpStatus { get; private set; }

        public void WriteError(
            ExitCode exitCode,
            FailureCategory category,
            string message,
            string? commandName,
            int? httpStatus = null,
            string? serverVersion = null,
            JsonNode? details = null
        )
        {
            ErrorCalled = true;
            LastExitCode = (int)exitCode;
            LastHttpStatus = httpStatus;
            LastErrorCategory = category.ToWire();
            LastErrorServerVersion = serverVersion;
        }

        public bool ListCalled { get; private set; }
        public ListPaging LastPaging { get; private set; }

        public void WriteList(
            IReadOnlyList<object> items,
            string[] headers,
            IEnumerable<string[]> rows,
            ListPaging paging,
            string? commandName = null,
            long? durationMs = null
        )
        {
            ListCalled = true;
            LastPaging = paging;
            LastTableCommand = commandName;
            LastTableDuration = durationMs;
        }

        public void WriteTable(
            string[] headers,
            IEnumerable<string[]> rows,
            string? commandName = null,
            long? durationMs = null
        )
        {
            TableCalled = true;
            LastTableCommand = commandName;
            LastTableDuration = durationMs;
        }

        public void WriteMessage(
            object data,
            string message,
            string? commandName = null,
            long? durationMs = null
        ) => MessageCalled = true;

        public IReadOnlyList<BulkItemResult>? LastBulk { get; private set; }

        public void WriteBulk(
            IReadOnlyList<BulkItemResult> results,
            string? commandName = null,
            long? durationMs = null
        ) => LastBulk = results;

        public void WriteDryRun(
            string method,
            string url,
            string? body,
            string? commandName,
            long? durationMs = null,
            IReadOnlyList<Umbraco.Cli.Infrastructure.Http.PreviewedRequest>? then = null
        )
        {
            DryRunCalled = true;
            LastDryRunThen = then;
        }

        public IReadOnlyList<Umbraco.Cli.Infrastructure.Http.PreviewedRequest>? LastDryRunThen
        {
            get;
            private set;
        }
    }

    [Fact]
    public void WriteDryRun_FollowUpRequests_ArePassedToTheInnerWriter()
    {
        // #353: --quiet must not drop the later steps of a multi-step dry run.
        var inner = new RecordingWriter();
        var quiet = new QuietOutputWriter(inner);

        quiet.WriteDryRun(
            "POST",
            "https://x/user",
            null,
            "user.create",
            then: [new("POST", "https://x/user/1/change-password", null)]
        );

        Assert.Single(inner.LastDryRunThen!);
    }

    [Fact]
    public void WriteMessage_IsSuppressed()
    {
        var inner = new RecordingWriter();
        var quiet = new QuietOutputWriter(inner);

        quiet.WriteMessage(new { id = "1" }, "Deleted.");

        Assert.False(inner.MessageCalled);
    }

    [Fact]
    public void WriteBulk_PassesTheResultsToTheInnerWriter()
    {
        // Review finding on #236: without this, --quiet fell back to WriteSuccess and reported
        // every bulk run as "success" again.
        var inner = new RecordingWriter();
        var results = new[] { new BulkItemResult("x", BulkItemStatus.Error, "boom") };

        new QuietOutputWriter(inner).WriteBulk(results);

        Assert.Same(results, inner.LastBulk);
    }

    [Fact]
    public void Success_Error_Table_DryRun_PassThrough()
    {
        var inner = new RecordingWriter();
        var quiet = new QuietOutputWriter(inner);

        quiet.WriteSuccess(new { id = 1 });
        quiet.WriteError(
            ExitCode.Failed,
            FailureCategory.ServerError,
            "boom",
            "content.list",
            500,
            "17.3.5"
        );
        quiet.WriteTable(
            ["Id"],
            [
                ["1"],
            ],
            "content.list",
            7
        );
        quiet.WriteDryRun("POST", "https://x", null, "content.create");

        Assert.True(inner.SuccessCalled);
        Assert.True(inner.ErrorCalled);
        Assert.True(inner.TableCalled);
        Assert.True(inner.DryRunCalled);
        // #137: the decorator forwards command/duration so quiet list output keeps full meta.
        Assert.Equal("content.list", inner.LastTableCommand);
        Assert.Equal(7, inner.LastTableDuration);
        // #152: the decorator forwards the failure category and server version too.
        Assert.Equal("server_error", inner.LastErrorCategory);
        Assert.Equal("17.3.5", inner.LastErrorServerVersion);
    }

    [Fact]
    public void Factory_QuietTrue_WrapsInQuietWriter()
    {
        var writer = OutputWriterFactory.Create(OutputFormat.Json, fields: null, quiet: true);
        Assert.IsType<QuietOutputWriter>(writer);
    }
}
