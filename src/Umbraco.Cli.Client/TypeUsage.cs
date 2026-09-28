namespace Umbraco.Cli.Client;

/// <summary>
/// What a document or media type delete would take with it (#287): Umbraco deletes every item of
/// the type, and removes the type's properties (and their values) from every type that uses it as a
/// composition. The delete guard refuses on any of these.
/// </summary>
/// <param name="Items">
/// The documents (or media items) of the type, counting the recycle bin, since deleting the type
/// deletes trashed items too.
/// </param>
/// <param name="ComposedBy">The names of the types that use this type as a composition.</param>
/// <param name="IsElement">
/// True for an element type. Its content lives inside block values of other documents, which no
/// endpoint reports, so it has no countable items (always false for a media type).
/// </param>
public sealed record TypeUsage(int Items, IReadOnlyList<string> ComposedBy, bool IsElement = false);
