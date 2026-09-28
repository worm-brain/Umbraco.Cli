using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Checks whether a delete would take or orphan something else, before it is sent
/// (#246, #252, #253, #269; docs/conventions.md 5.2).
/// <para>
/// Umbraco carries out these deletes by cascading them: a data type takes every property that uses
/// it and the values stored in them, a member type takes its members, a document or media type
/// takes every item of that type, a dictionary item takes its children, and a language takes every
/// variant and translation in it. Deleting a template, member group or user group leaves what used
/// it pointing at nothing. The backoffice warns first; the CLI must be at least as careful.
/// <see cref="ReasonAsync"/> is the per-item check the single deletes run (through
/// <see cref="Protect"/> and <see cref="ProtectEach"/>). <c>schema apply --prune</c> runs it too,
/// except where the plan changes the answer: a dictionary item's children, or a template's users,
/// that the same prune deletes. Those use <see cref="TemplateReason"/> and the prune's own
/// dictionary rule.
/// </para>
/// </summary>
public static class InUseGuard
{
    /// <summary>How many references a refusal lists before summarising the rest.</summary>
    private const int ListedReferences = 5;

    /// <summary>The option name that overrides a refusal.</summary>
    public const string ForceOption = "--force";

    /// <summary>
    /// Why deleting the <paramref name="kind"/> <paramref name="id"/> would destroy or orphan more
    /// than the item itself, or null when it is safe. A failed check is a reason too: an unknown is
    /// not a yes. Document and media types are checked by counting their items (#287); see
    /// <see cref="TypeUsageReason"/>. Languages are not id-keyed; see <see cref="LanguageReason"/>.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="kind">What the id names.</param>
    /// <param name="id">The item id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    public static Task<string?> ReasonAsync(
        IUmbracoManagementClient client,
        EntityKind kind,
        Guid id,
        CancellationToken ct
    ) =>
        kind switch
        {
            EntityKind.DataType => DataTypeAsync(client, id, ct),
            EntityKind.MemberType => MemberTypeAsync(client, id, ct),
            EntityKind.DocumentType => DocumentTypeAsync(client, id, ct),
            EntityKind.MediaType => MediaTypeAsync(client, id, ct),
            EntityKind.Template => TemplateAsync(client, id, ct),
            EntityKind.MemberGroup => MemberGroupAsync(client, id, ct),
            EntityKind.UserGroup => UserGroupAsync(client, id, ct),
            EntityKind.DictionaryItem => DictionaryItemAsync(client, id, ct),
            _ => Task.FromResult<string?>(null),
        };

    /// <summary>
    /// Why deleting template <paramref name="id"/> would leave document types without it, or null
    /// when none of <paramref name="users"/> uses it. The prune passes the users left once the
    /// document types it also deletes are taken out.
    /// </summary>
    /// <param name="id">The template id.</param>
    /// <param name="users">The document types that allow the template or default to it.</param>
    /// <returns>The reason, or null.</returns>
    public static string? TemplateReason(Guid id, IReadOnlyCollection<TemplateUser> users) =>
        users.Count == 0
            ? null
            : $"Template {id} is used by {Summarise([.. users.Select(u => u.Name)])}. Deleting it "
                + "leaves those document types, and the documents that render with it, without it.";

    /// <summary>
    /// Why deleting a language is never safe to do by default: Umbraco deletes every culture variant
    /// and dictionary translation in it, and has no endpoint that says whether there are any. So a
    /// language delete, and a language prune, always need <c>--force</c> (#269).
    /// </summary>
    /// <param name="isoCode">The language's ISO code.</param>
    /// <returns>The reason.</returns>
    public static string LanguageReason(string isoCode) =>
        $"Deleting language {isoCode} also deletes every culture variant and dictionary "
        + "translation in that language.";

    /// <summary>
    /// Adds <c>--force</c> to a delete command and has the executor refuse the delete, before it is
    /// confirmed, while <see cref="ReasonAsync"/> finds a reason for the item the argument names
    /// and <c>--force</c> is not given.
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="idArg">The command's <c>&lt;id|alias&gt;</c> argument; its kind picks the check.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what else the delete removes.</param>
    public static void Protect(Command command, ReferenceArgument idArg, string forceDescription) =>
        Refuse(
            command,
            async (parseResult, client, ct) =>
            {
                // A reference that does not resolve is not a safety question: the delete itself
                // then fails with the resolver's 404, which says what was not found.
                var id = await idArg.ResolveAsync(parseResult, client, ct);
                return id.IsSuccess ? await ReasonAsync(client, idArg.Kind, id.Data, ct) : null;
            },
            forceDescription
        );

    /// <summary>
    /// <see cref="Protect"/> for a delete of several items at once: the references are resolved
    /// once, each item is checked (in parallel), and every reason found is reported.
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="kind">What the references name.</param>
    /// <param name="idsArg">The command's variadic <c>&lt;id&gt;...</c> argument.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what else the delete removes.</param>
    public static void ProtectEach(
        Command command,
        EntityKind kind,
        Argument<string[]> idsArg,
        string forceDescription
    ) =>
        Refuse(
            command,
            async (parseResult, client, ct) =>
            {
                // A reference that does not resolve is left to the delete, which reports it.
                var ids = await client.ResolveIdsAsync(kind, parseResult.GetValue(idsArg)!, ct);
                if (!ids.IsSuccess)
                    return null;
                var reasons = await Task.WhenAll(
                    ids.Data!.Select(id => ReasonAsync(client, kind, id, ct))
                );
                return reasons.OfType<string>().ToList() is { Count: > 0 } found
                    ? string.Join(" ", found)
                    : null;
            },
            forceDescription
        );

    /// <summary>
    /// Adds <c>--force</c> to a delete that is never safe by default, because nothing can say what
    /// it would take with it (a language), and refuses the delete without it.
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="reason">The reason, from the parsed command line.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what else the delete removes.</param>
    public static void RequireForce(
        Command command,
        Func<ParseResult, string> reason,
        string forceDescription
    ) =>
        Refuse(
            command,
            (parseResult, _, _) => Task.FromResult<string?>(reason(parseResult)),
            forceDescription
        );

    /// <summary>
    /// Adds <c>--force</c> and registers the refusal: the executor runs <paramref name="reason"/>
    /// after connecting and before the confirmation prompt, and refuses when it returns one and
    /// <c>--force</c> is not given.
    /// </summary>
    private static void Refuse(
        Command command,
        Func<ParseResult, IUmbracoManagementClient, CancellationToken, Task<string?>> reason,
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
                var why = await reason(parseResult, client, ct);
                return why is null
                    ? null
                    : $"{why} Nothing was deleted. Re-run with {ForceOption} to delete it anyway.";
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

    /// <summary>Why deleting template <paramref name="id"/> would orphan document types, or null when none use it.</summary>
    private static async Task<string?> TemplateAsync(
        ITemplateClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var usage = await client.GetTemplateUsageAsync(ct);
        if (!usage.IsSuccess)
            return CouldNotCheck("template", id, usage.ErrorMessage);
        return TemplateReason(id, usage.Data!.GetValueOrDefault(id) ?? []);
    }

    /// <summary>Why deleting member group <paramref name="id"/> would drop members from it, or null when it is empty.</summary>
    private static async Task<string?> MemberGroupAsync(
        IMemberGroupClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var count = await client.CountMembersInGroupAsync(id, ct);
        if (!count.IsSuccess)
            return CouldNotCheck("member group", id, count.ErrorMessage);
        return count.Data == 0
            ? null
            : $"Member group {id} has {count.Data} member(s). Deleting it removes their "
                + "membership and any access rules that name the group.";
    }

    /// <summary>Why deleting user group <paramref name="id"/> would take access from users, or null when it is empty.</summary>
    private static async Task<string?> UserGroupAsync(
        IUserGroupClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var count = await client.CountUsersInGroupAsync(id, ct);
        if (!count.IsSuccess)
            return CouldNotCheck("user group", id, count.ErrorMessage);
        return count.Data == 0
            ? null
            : $"User group {id} has {count.Data} user(s). Deleting it takes away the sections "
                + "and permissions it grants them.";
    }

    /// <summary>Why deleting dictionary item <paramref name="id"/> would delete children, or null when it has none.</summary>
    private static async Task<string?> DictionaryItemAsync(
        IDictionaryClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var children = await client.GetDictionaryTreeAsync(id, 0, 1, ct);
        if (!children.IsSuccess)
            return CouldNotCheck("dictionary item", id, children.ErrorMessage);
        return children.Data!.Total == 0
            ? null
            : $"Dictionary item {id} has {children.Data.Total} child item(s). Deleting it also "
                + "deletes them and their translations.";
    }

    /// <summary>
    /// Why deleting document type <paramref name="id"/> would lose content, or null when nothing
    /// uses it (#287). Before, every document type delete needed <c>--force</c>, so it became
    /// routine and meant nothing when it mattered.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="id">The document type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    private static async Task<string?> DocumentTypeAsync(
        IDocumentTypeClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var usage = await client.GetDocumentTypeUsageAsync(id, ct);
        return usage.IsSuccess
            ? TypeUsageReason("document", id, usage.Data!)
            : CouldNotCheck("document type", id, usage.ErrorMessage);
    }

    /// <summary>The media twin of <see cref="DocumentTypeAsync"/> (#287).</summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="id">The media type id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    private static async Task<string?> MediaTypeAsync(
        IMediaTypeClient client,
        Guid id,
        CancellationToken ct
    )
    {
        var usage = await client.GetMediaTypeUsageAsync(id, ct);
        return usage.IsSuccess
            ? TypeUsageReason("media", id, usage.Data!)
            : CouldNotCheck("media type", id, usage.ErrorMessage);
    }

    /// <summary>
    /// Why deleting a document or media type with this <paramref name="usage"/> would lose more
    /// than the type, or null when it would not (#287). Umbraco deletes every item of the type,
    /// the recycle bin included, and removes the type's properties and their values from every
    /// type that uses it as a composition. An element type's content lives in block values that
    /// no endpoint reports, so it cannot be counted, and an unknown is not a yes.
    /// </summary>
    /// <param name="item">The item word: <c>document</c> or <c>media</c>.</param>
    /// <param name="id">The type id.</param>
    /// <param name="usage">What uses the type.</param>
    /// <returns>The reason, or null.</returns>
    internal static string? TypeUsageReason(string item, Guid id, TypeUsage usage)
    {
        var reasons = new List<string>();
        if (usage.Items > 0)
            reasons.Add(
                $"{Capitalised(item)} type {id} has {usage.Items} {item} item(s), counting the "
                    + "recycle bin. Deleting it also deletes them."
            );
        if (usage.ComposedBy.Count > 0)
            reasons.Add(
                $"{Capitalised(item)} type {id} is a composition of {Summarise([.. usage.ComposedBy])}. "
                    + "Deleting it removes its properties, and the values stored in them, from "
                    + "those types."
            );
        if (usage.IsElement)
            reasons.Add(
                $"{Capitalised(item)} type {id} is an element type. Block content in other "
                    + "documents can use it, and Umbraco does not report where, so the CLI "
                    + "cannot check."
            );
        return reasons.Count == 0 ? null : string.Join(" ", reasons);
    }

    /// <summary>The word with its first letter in upper case (<c>document</c> to <c>Document</c>).</summary>
    /// <param name="word">The word.</param>
    /// <returns>The capitalised word.</returns>
    private static string Capitalised(string word) => char.ToUpperInvariant(word[0]) + word[1..];

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
