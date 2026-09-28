using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Content.Bulk;

namespace Umbraco.Cli.Tests;

/// <summary>Tests for the bulk-operation id reader (#85).</summary>
public class BulkIdsTests
{
    [Fact]
    public void ReadsStdin_Dash_IsTrue()
    {
        // #312: '-' is the stdin token for every file input (docs/conventions.md 4.5).
        Assert.True(BulkIds.ReadsStdin(new FileInfo("-")));
    }

    [Fact]
    public void ReadsStdin_APath_IsFalse()
    {
        Assert.False(BulkIds.ReadsStdin(new FileInfo("ids.txt")));
    }

    [Fact]
    public void Read_File_TrimsWhitespaceAndSkipsBlankLines()
    {
        // #85: ids are one per line; surrounding whitespace is trimmed and blank lines ignored,
        // so a file with stray blank/indented lines still yields a clean id list in order.
        var path = Path.Combine(Path.GetTempPath(), $"bulk-ids-{Guid.NewGuid()}.txt");
        File.WriteAllText(path, "  id-one \n\n\t\nid-two\n   \nid-three\n");
        try
        {
            var ids = BulkIds.Read(new FileInfo(path));

            Assert.Equal(["id-one", "id-two", "id-three"], ids);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bulk-ids-{Guid.NewGuid()}.txt");
        File.WriteAllText(path, "   \n\n");
        try
        {
            Assert.Empty(BulkIds.Read(new FileInfo(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_StdinWithAByteOrderMark_ReadsTheFirstIdIntact()
    {
        // A producer that writes UTF-8 with a BOM (PowerShell, .NET's Encoding.UTF8) must not turn
        // the first id into garbage that fails as "not a valid GUID".
        var bytes = System
            .Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes("3f7a8b2e-1234-5678-abcd-ef0123456789\n"))
            .ToArray();
        using var reader = new StreamReader(
            new MemoryStream(bytes),
            new System.Text.UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true
        );

        var ids = Umbraco.Cli.Commands.Content.Bulk.BulkIds.Parse(reader);

        Assert.Equal(["3f7a8b2e-1234-5678-abcd-ef0123456789"], ids);
    }

    // ── #288: bulk reads the CLI's own output ────────────────────────────────

    private const string Id1 = "3f7a8b2e-1234-5678-abcd-ef0123456789";
    private const string Id2 = "9c1d2e3f-4321-8765-dcba-0123456789ab";

    /// <summary>Parses <paramref name="text"/> as if it were piped to stdin.</summary>
    private static IReadOnlyList<string> ParseText(string text) =>
        BulkIds.Parse(new StringReader(text));

    [Fact]
    public void Parse_JsonEnvelope_ReadsTheIdOfEachDataItem()
    {
        // What `umbraco content list --fields id` writes: an indented success envelope.
        var envelope = $$"""
            {
              "status": "success",
              "data": [
                { "id": "{{Id1}}" },
                { "id": "{{Id2}}", "name": "Home" }
              ],
              "meta": { "schemaVersion": "6", "command": "content.list" }
            }
            """;

        Assert.Equal([Id1, Id2], ParseText(envelope));
    }

    [Fact]
    public void Parse_JsonEnvelopeWithOneDataObject_ReadsItsId()
    {
        // A single-item command (get, create) writes `data` as one object.
        Assert.Equal([Id1], ParseText($$$"""{"status":"success","data":{"id":"{{{Id1}}}"}}"""));
    }

    [Fact]
    public void Parse_JsonArrayOfObjects_ReadsEachId()
    {
        Assert.Equal([Id1, Id2], ParseText($$"""  [{"id":"{{Id1}}"},{"id":"{{Id2}}"}]"""));
    }

    [Fact]
    public void Parse_JsonArrayOfStrings_ReadsEachString()
    {
        Assert.Equal([Id1, Id2], ParseText($"""["{Id1}", " {Id2} "]"""));
    }

    [Fact]
    public void Parse_JsonItemWithoutId_ThrowsNamingTheItem()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            ParseText($$"""{"data":[{"id":"{{Id1}}"},{"name":"Home"}]}""")
        );

        Assert.Contains(".data[1]", ex.Message);
    }

    [Fact]
    public void Parse_JsonObjectWithoutData_Throws()
    {
        Assert.Throws<InvalidInputException>(() => ParseText("""{"status":"success"}"""));
    }

    [Fact]
    public void Parse_UnparseableJson_Throws()
    {
        Assert.Throws<InvalidInputException>(() => ParseText($$"""{"data":[{"id":"{{Id1}}" """));
    }

    [Fact]
    public void Parse_CsvWithIdHeader_ReadsTheIdColumnAndSkipsTheHeader()
    {
        // What `-o csv` writes: CRLF or LF rows, a field with a comma or quote quoted (RFC 4180).
        var csv = $"name,id\r\n\"Home, sweet \"\"home\"\"\",{Id1}\r\nAbout,{Id2}\r\n";

        Assert.Equal([Id1, Id2], ParseText(csv));
    }

    [Fact]
    public void Parse_CsvWithOnlyAnIdColumn_SkipsTheHeader()
    {
        // `--fields id -o csv`: the header is the single word `id`, which is not an id.
        Assert.Equal([Id1, Id2], ParseText($"id\n{Id1}\n{Id2}\n"));
    }

    [Fact]
    public void Parse_CsvWithAQuotedNewlineInAnotherColumn_KeepsRowsAligned()
    {
        var csv = $"id,name\n{Id1},\"two\nlines\"\n{Id2},plain\n";

        Assert.Equal([Id1, Id2], ParseText(csv));
    }

    [Fact]
    public void Parse_CsvRowWithoutAnId_ThrowsNamingTheRow()
    {
        var ex = Assert.Throws<InvalidInputException>(() =>
            ParseText($"id,name\n{Id1},Home\n,About\n")
        );

        Assert.Contains("row 3", ex.Message);
    }

    [Fact]
    public void Parse_CsvWithAnUnclosedQuote_Throws()
    {
        Assert.Throws<InvalidInputException>(() => ParseText($"id,name\n{Id1},\"Home\n"));
    }

    [Fact]
    public void Parse_PlainLines_KeepsEveryLineAsAnId()
    {
        // Plain input is not mistaken for CSV: no line reads as an `id` header.
        Assert.Equal([Id1, "not-a-guid", Id2], ParseText($"{Id1}\r\nnot-a-guid\r\n{Id2}"));
    }

    [Fact]
    public void Read_FileHoldingTheJsonEnvelope_ReadsTheIds()
    {
        // --file takes the same formats as stdin (`umbraco content list -o json > ids.json`).
        var path = Path.Combine(Path.GetTempPath(), $"bulk-ids-{Guid.NewGuid()}.json");
        File.WriteAllText(path, $$"""{"status":"success","data":[{"id":"{{Id1}}"}]}""");
        try
        {
            Assert.Equal([Id1], BulkIds.Read(new FileInfo(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_MissingFile_ThrowsFileNotFound()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid()}"));

        Assert.Throws<FileNotFoundException>(() => BulkIds.Read(missing));
    }
}
