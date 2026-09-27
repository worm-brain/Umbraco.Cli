using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;
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
/// <see cref="ReasonAsync"/> is the one check, shared by the single deletes (through
/// <see cref="Protect(Command, string, ReferenceArgument, string)"/>) and by
/// <c>schema apply --prune</c>.
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
    /// not a yes. Document and media types always have a reason, because Umbraco has no endpoint
    /// that says how many items use one. Languages are not id-keyed; see <see cref="LanguageReason"/>.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="kind">A <see cref="SchemaKinds"/> value. Kinds with no check are always safe.</param>
    /// <param name="id">The item id.</param>
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
            SchemaKinds.Template => TemplateAsync(client, id, ct),
            SchemaKinds.MemberGroup => MemberGroupAsync(client, id, ct),
            SchemaKinds.UserGroup => UserGroupAsync(client, id, ct),
            SchemaKinds.DictionaryItem => DictionaryItemAsync(client, id, ct),
            _ => Task.FromResult<string?>(null),
        };

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
    /// Adds <c>--force</c> to a type-delete command and has the executor refuse the delete, before
    /// it is confirmed, while <see cref="ReasonAsync"/> finds a reason and <c>--force</c> is not given.
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="kind">The <see cref="SchemaKinds"/> value of the type it deletes.</param>
    /// <param name="idArg">The command's <c>&lt;id|alias&gt;</c> argument.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what it deletes along with the type.</param>
    public static void Protect(
        Command command,
        string kind,
        ReferenceArgument idArg,
        string forceDescription
    ) =>
        Protect(
            command,
            async (parseResult, client, ct) =>
            {
                // A reference that does not resolve is not a safety question: the delete itself
                // then fails with the resolver's 404, which says what was not found.
                var id = await idArg.ResolveAsync(parseResult, client, ct);
                return id.IsSuccess ? await ReasonAsync(client, kind, id.Data, ct) : null;
            },
            forceDescription
        );

    /// <summary>
    /// Adds <c>--force</c> to a delete command and has the executor refuse the delete, before it is
    /// confirmed, while <paramref name="reason"/> returns one and <c>--force</c> is not given. For
    /// deletes whose target is not a single <see cref="ReferenceArgument"/> (a language's ISO code,
    /// several user groups).
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="reason">Why the delete would take or orphan something else, or null when it is safe.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what else the delete removes.</param>
    public static void Protect(
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
        var users = await client.GetDocumentTypesUsingTemplateAsync(id, ct);
        if (!users.IsSuccess)
            return CouldNotCheck("template", id, users.ErrorMessage);
        return users.Data!.Count == 0
            ? null
            : $"Template {id} is used by {Summarise([.. users.Data!])}. Deleting it leaves "
                + "those document types, and the documents that render with it, without it.";
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
