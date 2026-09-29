namespace Umbraco.Cli.Tests;

/// <summary>
/// Assertions on how many HTTP requests a command made (#408). Request counts are deterministic,
/// so unlike timings they can fail a PR build: a change that turns one request into one per item
/// shows up here as a count that went up.
/// <para>
/// A failure names the command line, the expected and actual counts, how the expected count is
/// worked out, and every request that was sent, so the change that added a request can be read
/// straight off the build log.
/// </para>
/// </summary>
internal static class RequestCountAssert
{
    /// <summary>The page size of the CLI's own paging and of every tree walk.</summary>
    public const int PageSize = 100;

    /// <summary>
    /// How many page requests it takes to read <paramref name="items"/> items: one per
    /// <see cref="PageSize"/>, and one for an empty collection, which still has to be asked.
    /// </summary>
    /// <param name="items">How many items the collection holds.</param>
    /// <returns><c>max(1, ceil(items / 100))</c>.</returns>
    public static int PagesOf(int items) => Math.Max(1, (items + PageSize - 1) / PageSize);

    /// <summary>Asserts that the run succeeded and sent exactly <paramref name="expected"/> requests.</summary>
    /// <param name="run">The run.</param>
    /// <param name="expected">The expected number of requests.</param>
    /// <param name="formula">How <paramref name="expected"/> is made up, e.g. <c>"3 tree pages + 1 type read"</c>.</param>
    /// <exception cref="WireAssertionException">The run failed, or sent a different number of requests.</exception>
    public static void HasRequestCount(this CliRun run, int expected, string formula) =>
        run.HasRequestCount(_ => true, "HTTP", expected, formula);

    /// <summary>
    /// Asserts that the run succeeded and sent exactly <paramref name="expected"/> requests
    /// matching <paramref name="which"/>.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="which">Selects the requests to count, e.g. the token exchanges.</param>
    /// <param name="what">What the selected requests are, for the message, e.g. <c>"token-endpoint"</c>.</param>
    /// <param name="expected">The expected number of matching requests.</param>
    /// <param name="formula">How <paramref name="expected"/> is made up.</param>
    /// <exception cref="WireAssertionException">
    /// The run failed (a failed run's count says nothing about the command), or sent a different
    /// number of matching requests.
    /// </exception>
    public static void HasRequestCount(
        this CliRun run,
        Func<Recorded, bool> which,
        string what,
        int expected,
        string formula
    )
    {
        if (run.Exit != 0)
            throw new WireAssertionException(
                $"'{run.CommandLine}' failed with exit code {run.Exit}, so its request count says "
                    + $"nothing about the command. Fix the fixture first. stderr: {run.Stderr}"
            );

        var actual = run.Requests.Count(which);
        if (actual != expected)
            throw new WireAssertionException(
                $"'{run.CommandLine}' made {actual} {what} requests; expected {expected} "
                    + $"({formula}).{Environment.NewLine}Requests made:{Environment.NewLine}"
                    + string.Join(
                        Environment.NewLine,
                        run.Requests.Select(r => $"  {r.Method} {r.Uri.PathAndQuery}")
                    )
            );
    }
}
