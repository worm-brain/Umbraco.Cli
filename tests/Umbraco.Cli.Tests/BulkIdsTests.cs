using Umbraco.Cli.Commands.Content.Bulk;

namespace Umbraco.Cli.Tests;

/// <summary>Tests for the bulk-operation id reader (#85).</summary>
public class BulkIdsTests
{
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
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
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
}
