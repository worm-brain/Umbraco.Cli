namespace Umbraco.Cli.Client;

/// <summary>
/// Overlays requested entries onto current ones, matched on a key: an entry whose key is already
/// present is replaced, one that is not is appended.
/// <para>
/// Nearly every write path in the Management API is a <b>replace</b> - the PUT takes the whole
/// collection, so anything the client does not re-send is deleted (#178/#179). The defence is the
/// same shape each time (read, merge by whatever identifies an entry, send the merged set), and
/// it was written out longhand at three call sites: dictionary translations by ISO code, member
/// property values by alias/culture/segment, and domain bindings by hostname. Getting it subtly
/// wrong in one of them deletes a caller's data, so the three now share this.
/// </para>
/// </summary>
public static class MergeByKey
{
    /// <summary>Merges <paramref name="requested"/> into <paramref name="current"/> by key.</summary>
    /// <typeparam name="T">The entry type.</typeparam>
    /// <typeparam name="TKey">The type of the key that identifies an entry.</typeparam>
    /// <param name="current">The entries already on the entity; null is treated as empty.</param>
    /// <param name="requested">The entries to overlay, in the order given.</param>
    /// <param name="key">Extracts the identity of an entry.</param>
    /// <param name="comparer">
    /// How keys are compared. Null uses the default comparer - pass
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> for keys the API treats case-insensitively,
    /// such as ISO codes and hostnames.
    /// </param>
    /// <returns>
    /// A new list: the current entries in their original order, with matches replaced in place,
    /// followed by any entry whose key was not already there.
    /// </returns>
    public static List<T> Upsert<T, TKey>(
        IEnumerable<T>? current,
        IEnumerable<T> requested,
        Func<T, TKey> key,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        var cmp = comparer ?? EqualityComparer<TKey>.Default;
        var merged = (current ?? []).ToList();

        foreach (var entry in requested)
        {
            var incoming = key(entry);
            var existing = merged.FindIndex(m => cmp.Equals(key(m), incoming));
            if (existing >= 0)
                merged[existing] = entry;
            else
                merged.Add(entry);
        }

        return merged;
    }
}
