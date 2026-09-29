namespace Umbraco.Cli.Tests;

/// <summary>
/// The request-count assertion's own failures (#408): when a count goes up, the message names the
/// command and shows both counts, and a failed run is refused rather than counted.
/// </summary>
public class RequestCountAssertTests
{
    private static readonly Recorded Get = new(
        HttpMethod.Get,
        new Uri("https://site.test/umbraco/management/api/v1/tree/document/root?skip=0&take=100"),
        null
    );

    [Fact]
    public void HasRequestCount_MoreRequestsThanExpected_NamesTheCommandAndBothCounts()
    {
        // Arrange
        var run = new CliRun("content list", 0, "", "", [Get, Get, Get]);

        // Act
        var failure = Assert.Throws<WireAssertionException>(() =>
            run.HasRequestCount(2, "1 tree page + 1 type read")
        );

        // Assert
        Assert.StartsWith(
            "'content list' made 3 HTTP requests; expected 2 (1 tree page + 1 type read).",
            failure.Message
        );
    }

    [Fact]
    public void HasRequestCount_RunFailed_RefusesToCount()
    {
        // Arrange
        var run = new CliRun("content list", 1, "", "boom", [Get]);

        // Act
        var failure = Assert.Throws<WireAssertionException>(() => run.HasRequestCount(1, "1 page"));

        // Assert
        Assert.StartsWith("'content list' failed with exit code 1", failure.Message);
    }

    [Fact]
    public void HasRequestCount_ExpectedCount_Passes()
    {
        // Arrange
        var run = new CliRun("content list", 0, "", "", [Get]);

        // Act
        var failure = Record.Exception(() => run.HasRequestCount(1, "1 tree page"));

        // Assert
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 2)]
    [InlineData(250, 3)]
    public void PagesOf_ItemCount_IsOnePagePerHundredAndOneForNone(int items, int pages)
    {
        var actual = RequestCountAssert.PagesOf(items);

        Assert.Equal(pages, actual);
    }
}
