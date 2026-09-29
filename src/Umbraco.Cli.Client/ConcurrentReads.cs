using System.Runtime.ExceptionServices;

namespace Umbraco.Cli.Client;

/// <summary>
/// Runs independent reads a bounded number at a time (#422), for the export readers and the by-id
/// fallback of the batch reads. Results keep the input order, and a failure is reported exactly as
/// a one-at-a-time loop over the same items would report it:
/// <list type="bullet">
///   <item>Items are started in input order, never more than the limit at once.</item>
///   <item>Once any read fails (a failed response or an exception), no further item is started;
///   the reads already in flight are left to finish rather than cancelled, because an earlier
///   item may still be among them and it decides which failure is reported.</item>
///   <item>The failure reported is the one earliest in input order. Every item before it was
///   started and succeeded, so it is the failure the serial loop would have stopped at.</item>
/// </list>
/// A failure therefore costs at most <c>limit - 1</c> reads beyond the serial loop's, and nothing is
/// still running when the call returns.
/// </summary>
public static class ConcurrentReads
{
    /// <summary>
    /// Reads every item, at most <paramref name="limit"/> at a time, stopping at the first failed
    /// response (see the class summary for which failure is returned).
    /// </summary>
    /// <typeparam name="TIn">The item read.</typeparam>
    /// <typeparam name="TOut">What a read returns.</typeparam>
    /// <param name="items">The items, in the order results are wanted.</param>
    /// <param name="limit">The most reads in flight at once; at least 1.</param>
    /// <param name="read">Reads one item.</param>
    /// <param name="ct">Cancellation token, passed to every read.</param>
    /// <returns>Every result in input order, or the failure earliest in input order.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
    /// <exception cref="Exception">A read threw before any earlier item failed: rethrown as thrown.</exception>
    public static async Task<UmbracoResponse<IReadOnlyList<TOut>>> ReadAllAsync<TIn, TOut>(
        IReadOnlyList<TIn> items,
        int limit,
        Func<TIn, CancellationToken, Task<UmbracoResponse<TOut>>> read,
        CancellationToken ct
    )
    {
        var responses = await MapAsync(items, limit, read, r => !r.IsSuccess, ct);

        // Every slot up to the first failure is filled (see MapAsync), so the scan meets the
        // failure before any slot left empty by the stop.
        var results = new List<TOut>(items.Count);
        foreach (var response in responses)
        {
            if (!response.IsSuccess)
                return UmbracoResponse<IReadOnlyList<TOut>>.FailureFrom(response);
            results.Add(response.Data!);
        }
        return UmbracoResponse<IReadOnlyList<TOut>>.Success(results);
    }

    /// <summary>
    /// Maps every item, at most <paramref name="limit"/> at a time, stopping early when a read
    /// throws or returns a result <paramref name="stopWhen"/> matches.
    /// </summary>
    /// <typeparam name="TIn">The item read.</typeparam>
    /// <typeparam name="TOut">What a read returns.</typeparam>
    /// <param name="items">The items, in the order results are wanted.</param>
    /// <param name="limit">The most reads in flight at once; at least 1.</param>
    /// <param name="map">Reads one item.</param>
    /// <param name="stopWhen">
    /// Marks a result as a failure that stops the run; null when only an exception does.
    /// </param>
    /// <param name="ct">Cancellation token, passed to every read.</param>
    /// <returns>
    /// One result per item, in input order. After a stop, every slot before the first stopping
    /// result is filled and slots after it may hold <c>default</c> (items never started).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
    /// <exception cref="Exception">
    /// The exception of the earliest item that threw, when no earlier item stopped the run.
    /// </exception>
    public static async Task<TOut[]> MapAsync<TIn, TOut>(
        IReadOnlyList<TIn> items,
        int limit,
        Func<TIn, CancellationToken, Task<TOut>> map,
        Func<TOut, bool>? stopWhen,
        CancellationToken ct
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var results = new TOut[items.Count];
        var errors = new ExceptionDispatchInfo?[items.Count];
        var next = -1;
        var stopped = 0;

        // A fixed pool of workers, each claiming the next index, rather than one task per item
        // behind a semaphore: SemaphoreSlim does not promise FIFO wake-ups, and starting items in
        // input order is what makes the reported failure the serial loop's.
        async Task WorkAsync()
        {
            while (Volatile.Read(ref stopped) == 0)
            {
                var i = Interlocked.Increment(ref next);
                if (i >= items.Count)
                    return;
                try
                {
                    results[i] = await map(items[i], ct);
                    if (stopWhen?.Invoke(results[i]) == true)
                        Volatile.Write(ref stopped, 1);
                }
                catch (Exception e)
                {
                    errors[i] = ExceptionDispatchInfo.Capture(e);
                    Volatile.Write(ref stopped, 1);
                }
            }
        }

        await Task.WhenAll(
            Enumerable.Range(0, Math.Min(limit, items.Count)).Select(_ => WorkAsync())
        );

        // The earliest item that threw or stopped decides the outcome, as it would serially.
        for (var i = 0; i < items.Count; i++)
        {
            errors[i]?.Throw();
            if (stopWhen?.Invoke(results[i]) == true)
                break;
        }
        return results;
    }
}
