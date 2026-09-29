using Umbraco.Cli.Client;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Bounded concurrent reads (#422): results keep the input order whatever order the reads finish
/// in, no more than the limit run at once, and a failure is the one a one-at-a-time loop would
/// have stopped at. Each read here is a <see cref="TaskCompletionSource{T}"/> the test completes,
/// so the interleaving is set by the test rather than by timing.
/// </summary>
public sealed class ConcurrentReadsTests
{
    [Fact]
    public async Task ReadAllAsync_ReadsFinishInReverseOrder_ReturnsResultsInInputOrder()
    {
        // Arrange
        var reads = new Reads(3);
        var run = ConcurrentReads.ReadAllAsync([0, 1, 2], 3, reads.Read, CancellationToken.None);

        // Act
        reads.Succeed(2);
        reads.Succeed(1);
        reads.Succeed(0);
        var result = await run;

        // Assert
        Assert.Equal(["item 0", "item 1", "item 2"], result.Data!);
    }

    [Fact]
    public async Task ReadAllAsync_MoreItemsThanTheLimit_StartsOnlyTheLimitAtOnce()
    {
        // Arrange
        var reads = new Reads(5);

        // Act
        var run = ConcurrentReads.ReadAllAsync(
            [0, 1, 2, 3, 4],
            2,
            reads.Read,
            CancellationToken.None
        );
        var startedBeforeAnyFinished = reads.Started;
        for (var i = 0; i < 5; i++)
            reads.Succeed(i);
        await run;

        // Assert
        Assert.Equal([0, 1], startedBeforeAnyFinished);
    }

    [Fact]
    public async Task ReadAllAsync_LaterItemFailsFirst_ReturnsTheEarlierItemsFailure()
    {
        // Arrange: both fail, the later one first in time; serially the earlier one is reached first.
        var reads = new Reads(2);
        var run = ConcurrentReads.ReadAllAsync([0, 1], 2, reads.Read, CancellationToken.None);

        // Act
        reads.Fail(1, 500);
        reads.Fail(0, 404);
        var result = await run;

        // Assert
        Assert.Equal((false, 404), (result.IsSuccess, result.StatusCode));
    }

    [Fact]
    public async Task ReadAllAsync_ReadFails_StartsNoFurtherItems()
    {
        // Arrange
        var reads = new Reads(4);
        var run = ConcurrentReads.ReadAllAsync([0, 1, 2, 3], 2, reads.Read, CancellationToken.None);

        // Act
        reads.Fail(0, 500);
        reads.Succeed(1);
        await run;

        // Assert: item 1 was already in flight and is left to finish; 2 and 3 never start.
        Assert.Equal([0, 1], reads.Started);
    }

    [Fact]
    public async Task ReadAllAsync_ReadFails_ReturnsOnlyOnceTheReadsInFlightHaveFinished()
    {
        // Arrange
        var reads = new Reads(2);
        var run = ConcurrentReads.ReadAllAsync([0, 1], 2, reads.Read, CancellationToken.None);

        // Act
        reads.Fail(0, 500);
        var returnedBeforeTheOtherFinished = run.IsCompleted;
        reads.Succeed(1);
        await run;

        // Assert: nothing is left running (a media download writing into a deleted directory).
        Assert.False(returnedBeforeTheOtherFinished);
    }

    [Fact]
    public async Task ReadAllAsync_EarlierItemThrowsAfterALaterFailure_RethrowsTheEarlierException()
    {
        // Arrange
        var reads = new Reads(2);
        var run = ConcurrentReads.ReadAllAsync([0, 1], 2, reads.Read, CancellationToken.None);

        // Act
        reads.Fail(1, 500);
        reads.Throw(0, new HttpRequestException("connection reset"));

        // Assert
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => run);
        Assert.Equal("connection reset", thrown.Message);
    }

    [Fact]
    public async Task ReadAllAsync_NoItems_SucceedsWithNoResults()
    {
        // Act
        var result = await ConcurrentReads.ReadAllAsync<int, string>(
            [],
            8,
            (_, _) => throw new InvalidOperationException("No item to read."),
            CancellationToken.None
        );

        // Assert
        Assert.Equal((true, 0), (result.IsSuccess, result.Data!.Count));
    }

    [Fact]
    public async Task ReadAllAsync_LimitBelowOne_Throws()
    {
        // Act + Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ConcurrentReads.ReadAllAsync(
                [0],
                0,
                (_, _) => Task.FromResult(UmbracoResponse<string>.Success("")),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task MapAsync_NoStopCondition_ReturnsEveryResultInInputOrder()
    {
        // Arrange
        var reads = new Reads(2);
        var run = ConcurrentReads.MapAsync(
            [0, 1],
            2,
            reads.Read,
            stopWhen: null,
            CancellationToken.None
        );

        // Act: a failed response does not stop a run with no stop condition.
        reads.Succeed(1);
        reads.Fail(0, 404);
        var results = await run;

        // Assert
        Assert.Equal([false, true], results.Select(r => r.IsSuccess));
    }

    /// <summary>
    /// Reads whose completion the test controls: <see cref="Read"/> records the item as started
    /// and waits until the test succeeds, fails or throws it.
    /// </summary>
    /// <param name="count">How many items there are.</param>
    private sealed class Reads(int count)
    {
        private readonly TaskCompletionSource<UmbracoResponse<string>>[] _pending =
        [
            .. Enumerable
                .Range(0, count)
                .Select(_ => new TaskCompletionSource<UmbracoResponse<string>>()),
        ];

        private readonly List<int> _started = [];

        /// <summary>The items started so far, in the order they were started.</summary>
        public IReadOnlyList<int> Started
        {
            get
            {
                lock (_started)
                    return [.. _started];
            }
        }

        /// <summary>Starts reading an item: its result is whatever the test completes it with.</summary>
        /// <param name="item">The item.</param>
        /// <param name="ct">Unused.</param>
        /// <returns>The pending read.</returns>
        public Task<UmbracoResponse<string>> Read(int item, CancellationToken ct)
        {
            lock (_started)
                _started.Add(item);
            return _pending[item].Task;
        }

        /// <summary>Completes an item's read with <c>item {n}</c>.</summary>
        /// <param name="item">The item.</param>
        public void Succeed(int item) =>
            _pending[item].SetResult(UmbracoResponse<string>.Success($"item {item}"));

        /// <summary>Completes an item's read with a failed response.</summary>
        /// <param name="item">The item.</param>
        /// <param name="status">The failure's status code.</param>
        public void Fail(int item, int status) =>
            _pending[item]
                .SetResult(UmbracoResponse<string>.Failure(status, $"item {item} failed"));

        /// <summary>Makes an item's read throw.</summary>
        /// <param name="item">The item.</param>
        /// <param name="e">The exception.</param>
        public void Throw(int item, Exception e) => _pending[item].SetException(e);
    }
}
