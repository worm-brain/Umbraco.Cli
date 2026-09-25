using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary><c>sort --by</c> ordering (#232).</summary>
public class ChildSortTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static DateTimeOffset Day(int d) => new(2026, 9, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Order_ByName_IgnoresCase()
    {
        var order = ChildSort.Order(
            [new(A, "banana"), new(B, "Apple"), new(C, "cherry")],
            SortKey.Name,
            descending: false
        );

        Assert.Equal([B, A, C], order);
    }

    [Fact]
    public void Order_ByPublishDateDescending_PutsNewestFirstAndUnpublishedLast()
    {
        var order = ChildSort.Order(
            [
                new(A, "a", PublishDate: Day(1)),
                new(B, "b", PublishDate: null),
                new(C, "c", PublishDate: Day(3)),
            ],
            SortKey.PublishDate,
            descending: true
        );

        Assert.Equal([C, A, B], order);
    }

    [Fact]
    public void Order_Ties_KeepTheirCurrentOrder()
    {
        var order = ChildSort.Order(
            [new(A, "a", CreateDate: Day(2)), new(B, "b", CreateDate: Day(2))],
            SortKey.CreateDate,
            descending: false
        );

        Assert.Equal([A, B], order);
    }
}
