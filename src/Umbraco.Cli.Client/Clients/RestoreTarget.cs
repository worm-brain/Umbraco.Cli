namespace Umbraco.Cli.Client;

/// <summary>
/// Where <c>content restore</c> puts a document back (#230). The three cases are separate types, so
/// "a parent and the root at once" cannot be expressed.
/// </summary>
public abstract record RestoreTarget
{
    private RestoreTarget() { }

    /// <summary>The parent the item was trashed from (the root if it came from there).</summary>
    public static RestoreTarget Original { get; } = new OriginalParent();

    /// <summary>The tree root (content or media), whatever the original parent was.</summary>
    public static RestoreTarget Root { get; } = new ContentRoot();

    /// <summary>A parent the caller names.</summary>
    /// <param name="id">The parent id.</param>
    /// <returns>The target.</returns>
    public static RestoreTarget Under(Guid id) => new UnderParent(id);

    /// <summary>Restore under the original parent.</summary>
    public sealed record OriginalParent : RestoreTarget;

    /// <summary>Restore at the tree root.</summary>
    public sealed record ContentRoot : RestoreTarget;

    /// <summary>Restore under a named parent.</summary>
    /// <param name="Id">The parent id.</param>
    public sealed record UnderParent(Guid Id) : RestoreTarget;
}
