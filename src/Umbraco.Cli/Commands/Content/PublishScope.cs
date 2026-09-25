namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// What a publish or unpublish acts on (#223): the whole document, or named cultures of a
/// culture-variant one. The whole-document form is what an invariant document needs, and it is a
/// null culture list on both endpoints - publish enumerates the document's cultures itself, and
/// unpublish takes null as "all" - because the API has no wildcard (<c>"*"</c> is the invariant
/// culture, #158).
/// </summary>
/// <param name="Cultures">The culture codes, or null for the whole document.</param>
public sealed record PublishScope(IReadOnlyList<string>? Cultures)
{
    /// <summary>The whole document.</summary>
    public static readonly PublishScope WholeDocument = new((IReadOnlyList<string>?)null);

    /// <summary>
    /// The scope as <c>changes</c> entries (#229): <c>state</c> for the whole document, else
    /// <c>state[culture]</c> per culture.
    /// </summary>
    /// <returns>The paths.</returns>
    public IEnumerable<string> StatePaths() =>
        Cultures is null ? ["state"] : Cultures.Select(c => $"state[{c}]");
}
