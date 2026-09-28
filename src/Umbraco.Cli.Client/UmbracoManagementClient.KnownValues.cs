using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The one fail-closed "is this a value the instance knows?" check (#277), shared by the guards
/// that refuse input Umbraco would accept and then silently ignore: translation ISO codes (#181)
/// and webhook event aliases (#234).
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Refuses <paramref name="requested"/> unless every value is in the instance's known set.
    /// Blank values are rejected rather than filtered out (filtering would let them past the guard
    /// to be discarded by Umbraco, which is the behaviour being guarded against), and a known set
    /// that cannot be read is a failure to validate, not a pass: letting the request through would
    /// restore the silent-discard behaviour. Matching uses the comparer of the set that
    /// <paramref name="readKnown"/> returns, so each caller keeps its own (case-insensitive for ISO
    /// codes, ordinal for webhook aliases).
    /// </summary>
    /// <param name="requested">The values the caller asked for; nothing is checked when empty.</param>
    /// <param name="readKnown">Reads the instance's known values, or null when they cannot be read.</param>
    /// <param name="blankMessage">The message when a requested value is blank.</param>
    /// <param name="unreadableMessage">The message when the known set could not be read.</param>
    /// <param name="unknownMessage">Builds the message from the unknown values and the known set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidArgumentException">A value is blank or unknown (invalid_argument).</exception>
    /// <exception cref="ApiException">The known set could not be read (mapped to 400).</exception>
    private static async Task GuardKnownValuesAsync(
        IReadOnlyList<string> requested,
        Func<CancellationToken, Task<HashSet<string>?>> readKnown,
        string blankMessage,
        string unreadableMessage,
        Func<IReadOnlyList<string>, HashSet<string>, string> unknownMessage,
        CancellationToken ct
    )
    {
        if (requested.Count == 0)
            return;

        if (requested.Any(string.IsNullOrWhiteSpace))
            throw new InvalidArgumentException(blankMessage);

        var known = await readKnown(ct);
        if (known is null)
            throw BadRequest(unreadableMessage);

        var unknown = requested.Where(v => !known.Contains(v)).ToList();
        if (unknown.Count > 0)
            throw new InvalidArgumentException(unknownMessage(unknown, known));
    }
}
