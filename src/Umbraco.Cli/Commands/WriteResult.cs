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
    /// <summary>
    /// Awaits <paramref name="write"/> and, when it succeeded, publishes <paramref name="documentId"/>:
    /// the <c>--publish</c> step of <c>content version rollback</c> and <c>content restore</c> (#233),
    /// neither of which changes the live site on its own. A failed publish says the write itself
    /// landed, so a caller does not retry the write.
    /// </summary>
    /// <param name="write">The rollback or restore.</param>
    /// <param name="client">The client to publish with.</param>
    /// <param name="documentId">The document to publish.</param>
    /// <param name="cultures">Cultures to publish; null publishes every culture the document has.</param>
    /// <param name="done">What the write did, for the partial-failure message (e.g. "Rolled back").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The write's failure, the publish's failure, or a success.</returns>
    public static async Task<UmbracoResponse<Empty>> ThenPublishAsync(
        this Task<UmbracoResponse<Empty>> write,
        IUmbracoManagementClient client,
        Guid documentId,
        IEnumerable<string>? cultures,
        string done,
        CancellationToken ct
    )
    {
        var result = await write;
        if (!result.IsSuccess)
            return result;

        var published = await client.PublishContentAsync(documentId, cultures, ct: ct);
        return published.IsSuccess
            ? published
            : UmbracoResponse<Empty>.Failure(
                published.StatusCode,
                $"{done}, but publishing failed: {published.ErrorMessage} "
                    + $"Run 'umbraco content publish {documentId}' to retry the publish.",
                published.Category
            );
    }

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
