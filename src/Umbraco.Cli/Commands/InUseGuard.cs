using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Checks whether deleting a type would take content with it, before the delete is sent
/// (#246, #252, #253).
/// <para>
/// Umbraco carries out a type delete by cascading it: a data type takes every property that uses
/// it and the values stored in them, a member type takes its members, and a document or media type
/// takes every item of that type. The backoffice warns first; the CLI must be at least as careful.
/// <see cref="ReasonAsync"/> is the one check, shared by the single deletes (through
/// <see cref="Protect"/>) and by <c>schema apply --prune</c>.
/// </para>
/// </summary>
public static class InUseGuard
{
    /// <summary>How many references a refusal lists before summarising the rest.</summary>
    private const int ListedReferences = 5;

    /// <summary>The option name that overrides a refusal.</summary>
    public const string ForceOption = "--force";

    /// <summary>
    /// Why deleting the <paramref name="kind"/> <paramref name="id"/> would destroy more than the
    /// type itself, or null when it is safe. A failed check is a reason too: an unknown is not a
    /// yes. Document and media types always have a reason, because Umbraco has no endpoint that
    /// says how many items use one.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="kind">A <see cref="SchemaKinds"/> value. Kinds whose delete cascades to nothing (templates) are always safe.</param>
    /// <param name="id">The type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    public static Task<string?> ReasonAsync(
        IUmbracoManagementClient client,
        string kind,
        Guid id,
        CancellationToken ct
    ) =>
        kind switch
        {
            SchemaKinds.DataType => DataTypeAsync(client, id, ct),
            SchemaKinds.MemberType => MemberTypeAsync(client, id, ct),
            SchemaKinds.DocumentType => Task.FromResult<string?>(Uncountable("document", id)),
            SchemaKinds.MediaType => Task.FromResult<string?>(Uncountable("media", id)),
            _ => Task.FromResult<string?>(null),
        };

    /// <summary>
    /// Adds <c>--force</c> to a type-delete command and has the executor refuse the delete, before
    /// it is confirmed, while <see cref="ReasonAsync"/> finds a reason and <c>--force</c> is not given.
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="kind">The <see cref="SchemaKinds"/> value of the type it deletes.</param>
    /// <param name="idArg">The command's id argument.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what it deletes along with the type.</param>
    public static void Protect(
        Command command,
        string kind,
        Argument<Guid> idArg,
        string forceDescription
    )
    {
        var force = new Option<bool>(ForceOption) { Description = forceDescription };
        command.Add(force);
        command.RefuseWhen(
            async (parseResult, client, ct) =>
            {
                if (parseResult.GetValue(force))
                    return null;
                var reason = await ReasonAsync(client, kind, parseResult.GetValue(idArg), ct);
                return reason is null
                    ? null
                    : $"{reason} Nothing was deleted. Re-run with {ForceOption} to delete it anyway.";
            }
        );
    }

    /// <summary>Why deleting data type <paramref name="id"/> would lose content, or null when nothing uses it.</summary>
    private static async Task<string?> DataTypeAsync(
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

    /// <summary>Why deleting member type <paramref name="id"/> would lose members, or null when it has none.</summary>
    private static async Task<string?> MemberTypeAsync(
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

    /// <summary>The reason for a type whose items Umbraco cannot count (document, media).</summary>
    private static string Uncountable(string item, Guid id) =>
        $"Deleting {item} type {id} also deletes every {item} item of that type, and Umbraco "
        + "does not report how many there are, so the CLI cannot check.";

    /// <summary>The reason given when the usage check itself failed.</summary>
    private static string CouldNotCheck(string kind, Guid id, string? error) =>
        $"Could not check whether {kind} {id} is in use ({error ?? "no details"}).";

    /// <summary>
    /// Reads "Document type > property" names out of a <c>referenced-by</c> response
    /// (<c>{total, items: [{documentType: {name}, name}]}</c>). Items of other reference kinds
    /// fall back to their own name.
    /// </summary>
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
