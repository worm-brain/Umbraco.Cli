using System.Text;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>Redirected output is UTF-8 without a byte-order mark, so non-ASCII text survives a pipe.</summary>
public class ConsoleEncodingTests
{
    [Fact]
    public void Writer_NonAsciiText_IsWrittenAsUtf8()
    {
        // A name like this came out of a pipe as "Søg ??" once the JSON writer stopped escaping it (#311).
        using var stream = new MemoryStream();

        ConsoleEncoding.Writer(stream).Write("Søg 日本");

        Assert.Equal(Encoding.UTF8.GetBytes("Søg 日本"), stream.ToArray());
    }

    [Fact]
    public void Writer_WritesNoByteOrderMark()
    {
        // A BOM at the start of piped JSON breaks jq and other parsers.
        using var stream = new MemoryStream();

        ConsoleEncoding.Writer(stream).Write("{}");

        Assert.Equal("{}"u8.ToArray(), stream.ToArray());
    }
}
