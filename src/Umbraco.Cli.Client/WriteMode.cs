namespace Umbraco.Cli.Client;

/// <summary>
/// How an update lays its request over the item it writes (#189). Named, rather than a positional
/// <c>bool replace</c>, so a call site says what it does without the signature to hand.
/// </summary>
public enum WriteMode
{
    /// <summary>
    /// Read the item and lay the request over it, so anything the request leaves out keeps its
    /// value. The default for every update (conventions 5.1).
    /// </summary>
    Merge,

    /// <summary>
    /// Send the request as the whole of what it covers, dropping what it leaves out
    /// (<c>--replace</c>).
    /// </summary>
    Replace,
}

/// <summary>Conversions onto <see cref="WriteMode"/>.</summary>
public static class WriteModes
{
    /// <summary>The mode a <c>--replace</c> flag asks for.</summary>
    /// <param name="replace">Whether <c>--replace</c> was given.</param>
    /// <returns><see cref="WriteMode.Replace"/> when it was, <see cref="WriteMode.Merge"/> otherwise.</returns>
    public static WriteMode FromReplaceFlag(bool replace) =>
        replace ? WriteMode.Replace : WriteMode.Merge;
}
