using System.Text;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The one reader of standard input for every input that takes <c>-</c> (docs/conventions.md 4.5):
/// <c>--json-body</c>, <c>--content-file</c>, <c>--value-file</c>, the bulk id list and the content
/// and schema snapshot arguments (#443). It decodes UTF-8 and drops a leading byte-order mark, as
/// <see cref="File.ReadAllTextAsync(string, CancellationToken)"/> does for a file, so piping a file
/// and naming it give the same text.
/// <para>
/// It deliberately bypasses <see cref="Console.In"/>, which decodes with the console input code
/// page (often not UTF-8 on Windows, so "Søg" arrives garbled) and keeps a BOM as a character.
/// </para>
/// </summary>
public static class StandardInput
{
    /// <summary>
    /// Opens the raw standard-input stream; <see cref="Console.OpenStandardInput()"/> outside tests.
    /// Tests swap it for known bytes, because <see cref="Console.SetIn"/> replaces only
    /// <see cref="Console.In"/>, not the stream beneath it. It is process-global, so a test that
    /// sets it belongs in the <c>ConsoleCapture</c> collection and restores it afterwards.
    /// </summary>
    internal static Func<Stream> OpenStream { get; set; } = Console.OpenStandardInput;

    /// <summary>
    /// Opens a UTF-8 reader over standard input. As with a file, a UTF-16 or UTF-32 BOM switches
    /// the decoding to that encoding instead.
    /// </summary>
    /// <returns>The reader. Disposing it closes the stream.</returns>
    public static TextReader OpenText() =>
        new StreamReader(
            OpenStream(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true
        );

    /// <summary>Reads the whole of standard input as text, decoded as <see cref="OpenText"/> does.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The text, without a byte-order mark.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task<string> ReadToEndAsync(CancellationToken ct)
    {
        using var reader = OpenText();
        return await reader.ReadToEndAsync(ct);
    }
}
