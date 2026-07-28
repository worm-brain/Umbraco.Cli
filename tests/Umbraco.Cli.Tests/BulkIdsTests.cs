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
}
