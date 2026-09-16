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

        public void WriteSuccess<T>(T data, string? commandName = null, long? durationMs = null) =>
            SuccessCalled = true;

        public void WriteError(int code, string message) => ErrorCalled = true;

        public void WriteTable(string[] headers, IEnumerable<string[]> rows) => TableCalled = true;

        public void WriteMessage(string message) => MessageCalled = true;

        public void WriteDryRun(string method, string url, string? body) => DryRunCalled = true;
    }

    [Fact]
    public void WriteMessage_IsSuppressed()
    {
        var inner = new RecordingWriter();
        var quiet = new QuietOutputWriter(inner);

        quiet.WriteMessage("Deleted.");

        Assert.False(inner.MessageCalled);
    }

    [Fact]
    public void Success_Error_Table_DryRun_PassThrough()
    {
        var inner = new RecordingWriter();
        var quiet = new QuietOutputWriter(inner);

        quiet.WriteSuccess(new { id = 1 });
        quiet.WriteError(1, "boom");
        quiet.WriteTable(
            ["Id"],
            [
                ["1"],
            ]
        );
        quiet.WriteDryRun("POST", "https://x", null);

        Assert.True(inner.SuccessCalled);
        Assert.True(inner.ErrorCalled);
        Assert.True(inner.TableCalled);
        Assert.True(inner.DryRunCalled);
    }

    [Fact]
    public void Factory_QuietTrue_WrapsInQuietWriter()
    {
        var writer = OutputWriterFactory.Create(OutputFormat.Json, fields: null, quiet: true);
        Assert.IsType<QuietOutputWriter>(writer);
    }
}
