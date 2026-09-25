using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The <c>data</c> of a write that has no resulting item to show: what it acted on
/// (docs/conventions.md 6.2). Serialised as <c>{ "id": "..." }</c>.
/// </summary>
/// <param name="Id">The id (or, for an item keyed by name, path or ISO code, that key).</param>
public sealed record ItemRef(string Id)
{
    /// <summary>A reference to a GUID-keyed item.</summary>
    /// <param name="id">The item's id.</param>
    /// <returns>The reference.</returns>
    public static ItemRef Of(Guid id) => new(id.ToString());

    /// <summary>A reference to an item keyed by name, path or ISO code.</summary>
    /// <param name="key">The item's key.</param>
    /// <returns>The reference.</returns>
    public static ItemRef Of(string key) => new(key);
}

/// <summary>The <c>data</c> of a write that acted on several items: <c>{ "ids": [...] }</c>.</summary>
/// <param name="Ids">The ids, in the order acted on.</param>
public sealed record ItemRefs(IReadOnlyList<string> Ids)
{
    /// <summary>References to GUID-keyed items.</summary>
    /// <param name="ids">The ids.</param>
    /// <returns>The references.</returns>
    public static ItemRefs Of(IEnumerable<Guid> ids) => new([.. ids.Select(i => i.ToString())]);
}

/// <summary>Turns a write that returns nothing into one that returns its <c>data</c>.</summary>
public static class WriteResult
{
    /// <summary>On success, the result is <paramref name="data"/>; a failure carries through.</summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="write">The write.</param>
    /// <param name="data">What to report on success, e.g. <see cref="ItemRef.Of(Guid)"/>.</param>
    /// <returns>The write's outcome, carrying <paramref name="data"/>.</returns>
    public static async Task<UmbracoResponse<T>> Then<T>(
        this Task<UmbracoResponse<Empty>> write,
        T data
    )
    {
        var result = await write;
        return result.IsSuccess
            ? UmbracoResponse<T>.Success(data)
            : UmbracoResponse<T>.FailureFrom(result);
    }

    /// <summary>
    /// On success, the result is what <paramref name="read"/> returns - the resulting item, read
    /// back so the output shows it as <c>get</c> would; a failure of either carries through.
    /// </summary>
    /// <typeparam name="TWrite">What the write itself returns (discarded).</typeparam>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="write">The write.</param>
    /// <param name="read">Reads the item after the write.</param>
    /// <returns>The resulting item, or the failure.</returns>
    public static async Task<UmbracoResponse<T>> ThenRead<TWrite, T>(
        this Task<UmbracoResponse<TWrite>> write,
        Func<Task<UmbracoResponse<T>>> read
    )
    {
        var result = await write;
        return result.IsSuccess ? await read() : UmbracoResponse<T>.FailureFrom(result);
    }
}
