using System.Text;

namespace Umbraco.Cli.Infrastructure;

/// <summary>Makes the CLI write UTF-8 to stdout and stderr, whether or not they are redirected.</summary>
public static class ConsoleEncoding
{
    /// <summary>UTF-8 without a byte-order mark: a BOM at the start of piped JSON breaks parsers such as jq.</summary>
    internal static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );

    /// <summary>
    /// Switches stdout and stderr to UTF-8.
    /// <para>
    /// On a terminal, the console code page is set (issue #49: the Windows default showed "Søg" as
    /// "S?g"). A redirected stream never sees that code page, so on Windows .NET encoded piped output
    /// with the OEM code page, and anything outside it came out as "?" (a name like "日本" became
    /// "??"). That stayed hidden while the JSON writer escaped every non-ASCII character as
    /// <c>\uXXXX</c>; since #311 it writes them as themselves, so a redirected stream gets its own
    /// UTF-8 writer here. Pipes are where agents and scripts read the CLI, and they expect UTF-8.
    /// </para>
    /// </summary>
    public static void UseUtf8()
    {
        // Setting the console code page can throw when there is no attached console; that only
        // affects the terminal case, which then keeps its default.
        try
        {
            if (!Console.IsOutputRedirected || !Console.IsErrorRedirected)
                Console.OutputEncoding = Utf8;
        }
        catch (IOException)
        {
            // No attached console (or it rejected the change); safe to ignore.
        }

        if (Console.IsOutputRedirected)
            Console.SetOut(Writer(Console.OpenStandardOutput()));
        if (Console.IsErrorRedirected)
            Console.SetError(Writer(Console.OpenStandardError()));
    }

    /// <summary>A UTF-8 writer over a standard stream that flushes each write, as the console's own writers do.</summary>
    /// <param name="stream">The standard output or error stream.</param>
    /// <returns>The writer.</returns>
    internal static TextWriter Writer(Stream stream) =>
        TextWriter.Synchronized(new StreamWriter(stream, Utf8) { AutoFlush = true });
}
