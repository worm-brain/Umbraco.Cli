using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Checks whether deleting a type would take content with it, before the delete is sent
/// (#246, #252, #253).
/// <para>
/// Umbraco carries out a type delete by cascading it: a data type takes every property that uses
/// it and the values stored in them, a member type takes its members, and a document or media type
/// takes every item of that type. The backoffice warns first; the CLI must be at least as careful.
/// Each check returns why the delete would destroy more than the type itself, or null when it is
/// safe. <see cref="Refuse"/> turns a reason into a refusal unless the caller passed
/// <c>--force</c>.
/// </para>
/// </summary>
public static class InUseGuard
{
    /// <summary>How many references a refusal lists before summarising the rest.</summary>
    private const int ListedReferences = 5;

    /// <summary>The option name that overrides a refusal.</summary>
    public const string ForceOption = "--force";

    /// <summary>
    /// Why deleting data type <paramref name="id"/> would lose content, or null when nothing uses it.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="id">The data type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    public static async Task<string?> DataTypeAsync(
        IDataTypeClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var used = await client.IsDataTypeUsedAsync(id, ct);
        if (!used.IsSuccess)
            return CouldNotCheck("data type", id, used.ErrorMessage);
        if (!used.Data)
            return null;

        // Name what uses it when the reference list can be read; the refusal stands either way.
        var references = await client.GetDataTypeReferencedByRawAsync(id, 0, 100, ct);
        var names = references.IsSuccess ? ReferenceNames(references.Data) : [];
        var usedBy = names.Count == 0 ? "" : $" by {Summarise(names)}";
        return $"Data type {id} is in use{usedBy}. Deleting it also deletes every property that "
            + "uses it and all the values stored in those properties.";
    }

    /// <summary>
    /// Why deleting member type <paramref name="id"/> would lose members, or null when it has none.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="id">The member type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    public static async Task<string?> MemberTypeAsync(
        IMemberTypeClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var count = await client.CountMembersOfTypeAsync(id, ct);
        if (!count.IsSuccess)
            return CouldNotCheck("member type", id, count.ErrorMessage);
        return count.Data == 0
            ? null
            : $"Member type {id} has {count.Data} member(s). Deleting it also deletes them.";
    }

    /// <summary>
    /// Why deleting a document type is always refused without <c>--force</c>. Umbraco has no
    /// endpoint that says how many documents use a document type, so the CLI cannot tell a safe
    /// delete from one that empties a site.
    /// </summary>
    /// <param name="id">The document type id.</param>
    /// <returns>The reason.</returns>
    public static string DocumentType(Guid id) =>
        $"Deleting document type {id} also deletes every document of that type, and Umbraco does "
        + "not report how many there are, so the CLI cannot check.";

    /// <summary>
    /// Why deleting a media type is always refused without <c>--force</c> (see
    /// <see cref="DocumentType"/>).
    /// </summary>
    /// <param name="id">The media type id.</param>
    /// <returns>The reason.</returns>
    public static string MediaType(Guid id) =>
        $"Deleting media type {id} also deletes every media item of that type, and Umbraco does "
        + "not report how many there are, so the CLI cannot check.";

    /// <summary>Refuses the operation for <paramref name="reason"/>, unless <paramref name="force"/> was given.</summary>
    /// <param name="reason">Why the operation is unsafe, or null when it is safe.</param>
    /// <param name="force">Whether <c>--force</c> was passed.</param>
    /// <exception cref="SafetyRefusalException">There is a reason and <paramref name="force"/> is false.</exception>
    public static void Refuse(string? reason, bool force)
    {
        if (reason is not null && !force)
            throw new SafetyRefusalException(
                $"{reason} Nothing was deleted. Re-run with {ForceOption} to delete it anyway."
            );
    }

    /// <summary>The reason given when the usage check itself failed: an unknown is not a yes.</summary>
    private static string CouldNotCheck(string kind, Guid id, string? error) =>
        $"Could not check whether {kind} {id} is in use ({error ?? "no details"}).";

    /// <summary>
    /// Reads "Document type > property" names out of a <c>referenced-by</c> response
    /// (<c>{total, items: [{documentType: {name}, name}]}</c>). Items of other reference kinds
    /// fall back to their own name.
    /// </summary>
    /// <param name="response">The raw response.</param>
    /// <returns>The names, possibly empty.</returns>
    private static List<string> ReferenceNames(JsonNode? response) =>
        (response?["items"] as JsonArray ?? [])
            .Select(item =>
            {
                var owner = item?["documentType"]?["name"]?.GetValue<string>();
                var name = item?["name"]?.GetValue<string>();
                return owner is null ? name : $"{owner} > {name}";
            })
            .OfType<string>()
            .ToList();

    /// <summary>Lists up to <see cref="ListedReferences"/> names, then "and N more".</summary>
    private static string Summarise(List<string> names) =>
        names.Count <= ListedReferences
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(ListedReferences))
                + $" and {names.Count - ListedReferences} more";
}
