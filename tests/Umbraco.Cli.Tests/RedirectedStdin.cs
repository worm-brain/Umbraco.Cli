using System.Text;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Feeds fixed bytes to <see cref="StandardInput"/> for the lifetime of the instance, as a pipe
/// would, and restores the real stdin on dispose. Process-global, so use it only from a
/// <c>[Collection("ConsoleCapture")]</c> test.
/// </summary>
internal sealed class RedirectedStdin : IDisposable
{
    private readonly Func<Stream> _previousStream = StandardInput.OpenStream;
    private readonly TextReader _previousIn = Console.In;

    /// <summary>Redirects stdin to <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The raw bytes a producer would pipe in.</param>
    public RedirectedStdin(byte[] bytes)
    {
        StandardInput.OpenStream = () => new MemoryStream(bytes);
        // Console.In reads nothing, so an input that goes back to Console.In fails its test
        // rather than waiting on the test host's own stdin.
        Console.SetIn(TextReader.Null);
    }

    /// <summary>The UTF-8 bytes of <paramref name="text"/>, as an editor or shell would write them.</summary>
    /// <param name="text">The text.</param>
    /// <param name="byteOrderMark">Whether to start with the UTF-8 BOM (PowerShell, Notepad).</param>
    /// <returns>The bytes.</returns>
    public static byte[] Utf8(string text, bool byteOrderMark) =>
        [.. byteOrderMark ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(text)];

    /// <summary>Restores the real stdin.</summary>
    public void Dispose()
    {
        StandardInput.OpenStream = _previousStream;
        Console.SetIn(_previousIn);
    }
}
