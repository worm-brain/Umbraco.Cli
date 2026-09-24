using System.Text.Json;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The list output contract at schemaVersion 3 (#164/#173).
/// <para>
/// Before this, a list's JSON was built from the human table cells: every value was a string, and
/// the keys were derived from the column captions, so <c>content list</c> said
/// <c>"published": "True"</c> where <c>content get</c> said <c>"isPublished": true</c>. Structured
/// output is now serialized from the items themselves, which is what makes the two agree by
/// construction rather than by each command remembering to.
/// </para>
/// </summary>
[Collection("ConsoleCapture")]
public class ListContractTests
{
    private readonly JsonOutputWriter _writer = new();

    /// <summary>An item with the shapes that used to be flattened to strings.</summary>
    /// <param name="Id">An id.</param>
    /// <param name="Name">A name.</param>
    /// <param name="IsPublished">A boolean, which must stay a boolean.</param>
    /// <param name="Count">A number, which must stay a number.</param>
    private sealed record Item(string Id, string Name, bool IsPublished, int Count);

    /// <summary>Captures stdout while <paramref name="action"/> runs.</summary>
    /// <param name="action">The action to run.</param>
    /// <returns>What it wrote to stdout.</returns>
    private static string CaptureOut(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    /// <summary>Writes a two-item list and returns the parsed envelope.</summary>
    /// <param name="paging">The paging facts to emit.</param>
    /// <returns>The parsed envelope root.</returns>
    private JsonElement Write(ListPaging paging)
    {
        var items = new List<object>
        {
            new Item("1", "Alpha", true, 3),
            new Item("2", "Beta", false, 0),
        };
        var json = CaptureOut(() =>
            _writer.WriteList(
                items,
                ["ID", "Name", "Published", "Count"],
                items.Cast<Item>().Select(i => new[] { i.Id, i.Name, "yes", "3" }),
                paging,
                "content.list",
                12
            )
        );
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void WriteList_SerializesTheItems_NotTheTableCells()
    {
        var data = Write(ListPaging.Unknown).GetProperty("data");

        // The keys come from the DTO, so they match what `get` emits for the same type.
        var first = data[0];
        Assert.Equal("Alpha", first.GetProperty("name").GetString());
        Assert.True(first.TryGetProperty("isPublished", out _));
        Assert.False(first.TryGetProperty("published", out _));
    }

    [Fact]
    public void WriteList_KeepsBooleansAndNumbersAsTheirOwnTypes()
    {
        var data = Write(ListPaging.Unknown).GetProperty("data");

        // The old pipeline typed every cell as string, so this was "True" and "3".
        Assert.Equal(JsonValueKind.True, data[0].GetProperty("isPublished").ValueKind);
        Assert.Equal(JsonValueKind.False, data[1].GetProperty("isPublished").ValueKind);
        Assert.Equal(3, data[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public void WriteList_WithPaging_ReportsTotalAndHasMore()
    {
        var meta = Write(new ListPaging(37, 0, 20)).GetProperty("meta");

        // #173: 20 of 37 used to look exactly like all 37 - which is how Textstring and Richtext
        // went missing from a listing during the 2026-09-23 round without anything looking wrong.
        Assert.Equal(37, meta.GetProperty("total").GetInt32());
        Assert.Equal(0, meta.GetProperty("skip").GetInt32());
        Assert.Equal(20, meta.GetProperty("take").GetInt32());
        Assert.True(meta.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public void WriteList_OnTheLastPage_SaysThereIsNoMore()
    {
        // The fixture writes two items, so a last page is one that starts at 20 of a total 22.
        var meta = Write(new ListPaging(22, 20, 20)).GetProperty("meta");

        Assert.False(meta.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public void WriteList_WhenTheSourceCannotCount_OmitsTotalRatherThanGuessing()
    {
        var meta = Write(ListPaging.Unknown).GetProperty("meta");

        // "probably not" is exactly the guess that hides a truncated list, so an unknown total
        // omits hasMore too rather than reporting false.
        Assert.False(meta.TryGetProperty("total", out _));
        Assert.False(meta.TryGetProperty("hasMore", out _));
    }

    [Fact]
    public void WriteList_CarriesTheSameMetaAsAnObjectResult()
    {
        var meta = Write(ListPaging.Unknown).GetProperty("meta");

        Assert.Equal("content.list", meta.GetProperty("command").GetString());
        Assert.Equal(12, meta.GetProperty("durationMs").GetInt32());
        Assert.Equal("3", meta.GetProperty("schemaVersion").GetString());
    }

    [Theory]
    [InlineData(37, 0, 20, 20, true)]
    [InlineData(37, 20, 20, 17, false)]
    [InlineData(20, 0, 20, 20, false)]
    [InlineData(0, 0, 20, 0, false)]
    // A page that came back short of what was asked for: counting the request would say there is
    // more, counting what arrived says there is not.
    [InlineData(10, 0, 20, 10, false)]
    public void HasMore_CountsWhatWasDelivered(
        int total,
        int skip,
        int take,
        int delivered,
        bool expected
    ) => Assert.Equal(expected, new ListPaging(total, skip, take).HasMoreAfter(delivered));

    [Fact]
    public void HasMore_IsUnknownWhenTheTotalIs() =>
        Assert.Null(new ListPaging(null, 0, 20).HasMoreAfter(20));
}
