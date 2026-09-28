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
/// <see cref="ReasonAsync"/> is the one place that decides (#281). The single deletes run it with
/// no plan (through <see cref="Protect(Command, ReferenceArgument, string)"/> and
/// <see cref="ProtectEach"/>); <c>schema apply --prune</c> runs it for every planned delete with
/// the plan (<see cref="DeletePlanContext"/>), because the plan changes some answers: a dictionary
/// item's children, or a template's users, that the same prune deletes are expected to go.
/// </para>
/// </summary>
public static class InUseGuard
{
    /// <summary>How many references a refusal lists before summarising the rest.</summary>
    private const int ListedReferences = 5;

    /// <summary>The option name that overrides a refusal.</summary>
    public const string ForceOption = "--force";

    /// <summary>
    /// Why deleting <paramref name="target"/> would destroy or orphan more than the item itself, or
    /// null when it is safe. A failed check is a reason too: an unknown is not a yes. Document and
    /// media types are checked by counting their items (#287); see <see cref="TypeUsageReason"/>.
    /// <para>
    /// With a <paramref name="plan"/> (a prune), what the plan also deletes or moves away does not
    /// count against the target: a template's users the prune deletes, a dictionary item's
    /// children it deletes or moves, and a static file is checked against the templates as the
    /// plan leaves them. Where the plan changes what is being asked, the message says so (a
    /// dictionary item's children "the snapshot keeps"); where the question is the same, so is
    /// the message.
    /// </para>
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="target">What the delete removes.</param>
    /// <param name="plan">The rest of the prune plan, or null for a single delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    public static async Task<string?> ReasonAsync(
        IUmbracoManagementClient client,
        DeleteTarget target,
        DeletePlanContext? plan,
        CancellationToken ct
    ) =>
        target switch
        {
            DeleteTarget.Language language => LanguageReason(language.IsoCode),
            // A folder holds nothing the snapshot keeps (the diff implies those folders), and
            // Umbraco refuses to delete a folder that is not empty, so only files are checked.
            DeleteTarget.StaticFile { IsFolder: true } => null,
            DeleteTarget.StaticFile file => FileReason(
                file.Kind,
                file.Path,
                await (plan ?? DeletePlanContext.Nothing()).TemplatesAfterAsync(client, ct)
            ),
            DeleteTarget.Item { Kind: EntityKind.Template } item => await TemplateAsync(
                client,
                item.Id,
                plan,
                ct
            ),
            DeleteTarget.Item { Kind: EntityKind.DictionaryItem } item => plan is null
                ? await DictionaryItemAsync(client, item.Id, ct)
                : await KeptChildrenAsync(client, item, plan, ct),
            DeleteTarget.Item { Kind: EntityKind.DataType } item => await DataTypeAsync(
                client,
                item.Id,
                ct
            ),
            DeleteTarget.Item { Kind: EntityKind.MemberType } item => await MemberTypeAsync(
                client,
                item.Id,
                ct
            ),
            DeleteTarget.Item { Kind: EntityKind.DocumentType } item => await DocumentTypeAsync(
                client,
                item,
                ct
            ),
            DeleteTarget.Item { Kind: EntityKind.MediaType } item => await MediaTypeAsync(
                client,
                item,
                ct
            ),
            DeleteTarget.Item { Kind: EntityKind.MemberGroup } item => await MemberGroupAsync(
                client,
                item.Id,
                ct
            ),
            DeleteTarget.Item { Kind: EntityKind.UserGroup } item => await UserGroupAsync(
                client,
                item.Id,
                ct
            ),
            _ => null,
        };

    /// <summary>
    /// Why deleting a language is never safe to do by default: Umbraco deletes every culture variant
    /// and dictionary translation in it, and has no endpoint that says whether there are any. So a
    /// language delete, and a language prune, always need <c>--force</c> (#269).
    /// </summary>
    /// <param name="isoCode">The language's ISO code.</param>
    /// <returns>The reason.</returns>
    private static string LanguageReason(string isoCode) =>
        $"Deleting language {isoCode} also deletes every culture variant and dictionary "
        + "translation in that language.";

    /// <summary>
    /// Why pruning a static file would break a template (#292), or null when no template names
    /// it. A template "names" a file when its content contains the file name, or, for a partial
    /// view, its path without the extension in quotes (<c>Html.PartialAsync("header")</c>); see
    /// <see cref="Schema.SchemaStaticFiles.SearchTerms"/>. A text search can miss a reference
    /// built at run time, and can match a longer name, but it errs towards asking for
    /// <c>--force</c>.
    /// </summary>
    /// <param name="kind">The file's kind.</param>
    /// <param name="path">The file path.</param>
    /// <param name="templates">The templates after the plan, or null when they could not be read.</param>
    /// <returns>The reason, or null.</returns>
    internal static string? FileReason(
        StaticFileKind kind,
        string path,
        IReadOnlyList<(string Name, string Content)>? templates
    )
    {
        if (templates is null)
            return $"Could not read the templates to check whether '{path}' is in use.";
        var users = templates
            .Where(t => Schema.SchemaStaticFiles.Mentions(t.Content, kind, path))
            .Select(t => $"'{t.Name}'")
            .ToList();
        return users.Count == 0
            ? null
            : $"'{path}' is named by template(s) {string.Join(", ", users)}; deleting it breaks them.";
    }

    /// <summary>
    /// Why a prune's delete of a dictionary item would also delete children the snapshot keeps
    /// under it, or null when every child is pruned too, moved elsewhere by the same apply
    /// (updates run before deletes), or there are none. It asks about the children the plan
    /// keeps, not all of them, which is why its message differs from the single delete's.
    /// </summary>
    /// <param name="client">The client to read the tree with.</param>
    /// <param name="item">The dictionary item.</param>
    /// <param name="plan">The prune plan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    private static async Task<string?> KeptChildrenAsync(
        ISchemaClient client,
        DeleteTarget.Item item,
        DeletePlanContext plan,
        CancellationToken ct
    )
    {
        var liveChildren = await plan.DictionaryChildrenAsync(client, ct);
        if (liveChildren is null)
            return $"Could not read the dictionary tree to check whether '{item.Identity}' "
                + "has children.";
        if (!liveChildren.TryGetValue(item.Id, out var children))
            return null;
        var kept = children.Count(c =>
            !plan.Deletes(EntityKind.DictionaryItem, c) && !plan.MovesAway(c)
        );
        return kept == 0
            ? null
            : $"Dictionary item '{item.Identity}' has {kept} child item(s) the snapshot keeps. "
                + "Deleting it also deletes them.";
    }

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
                return id.IsSuccess
                    ? await ReasonAsync(
                        client,
                        new DeleteTarget.Item(idArg.Kind, id.Data),
                        plan: null,
                        ct
                    )
                    : null;
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
                    ids.Data!.Select(id =>
                        ReasonAsync(client, new DeleteTarget.Item(kind, id), plan: null, ct)
                    )
                );
                return reasons.OfType<string>().ToList() is { Count: > 0 } found
                    ? string.Join(" ", found)
                    : null;
            },
            forceDescription
        );

    /// <summary>
    /// <see cref="Protect(Command, ReferenceArgument, string)"/> for a delete whose target is not
    /// an id-keyed reference, such as a language named by its ISO code (which is never safe by
    /// default, because nothing can say what it would take with it).
    /// </summary>
    /// <param name="command">The delete command.</param>
    /// <param name="target">The target, from the parsed command line.</param>
    /// <param name="forceDescription">Help text for <c>--force</c>, saying what else the delete removes.</param>
    public static void Protect(
        Command command,
        Func<ParseResult, DeleteTarget> target,
        string forceDescription
    ) =>
        Refuse(
            command,
            (parseResult, client, ct) => ReasonAsync(client, target(parseResult), plan: null, ct),
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

    /// <summary>
    /// Why deleting template <paramref name="id"/> would leave document types without it, or null
    /// when none use it. A document type the <paramref name="plan"/> also deletes does not count,
    /// and the usage is read once per plan.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="id">The template id.</param>
    /// <param name="plan">The rest of the prune plan, or null for a single delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    private static async Task<string?> TemplateAsync(
        ITemplateClient client,
        Guid id,
        DeletePlanContext? plan,
        CancellationToken ct
    )
    {
        var usage = plan is null
            ? await client.GetTemplateUsageAsync(ct)
            : await plan.TemplateUsageAsync(client, ct);
        if (!usage.IsSuccess)
            return CouldNotCheck("template", id, usage.ErrorMessage);
        var users = (usage.Data!.GetValueOrDefault(id) ?? [])
            .Where(u => plan?.Deletes(EntityKind.DocumentType, u.DocumentTypeId) != true)
            .Select(u => u.Name)
            .ToList();
        return users.Count == 0
            ? null
            : $"Template {id} is used by {Summarise(users)}. Deleting it leaves those document "
                + "types, and the documents that render with it, without it.";
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
    /// Why deleting document type <paramref name="item"/> would lose content, or null when nothing
    /// uses it (#287). Before, every document type delete needed <c>--force</c>, so it became
    /// routine and meant nothing when it mattered.
    /// </summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="item">The document type: its id, and its alias when known (a prune, #396).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    private static async Task<string?> DocumentTypeAsync(
        IDocumentTypeClient client,
        DeleteTarget.Item item,
        CancellationToken ct
    )
    {
        var usage = await client.GetDocumentTypeUsageAsync(item.Id, ct);
        return usage.IsSuccess
            ? TypeUsageReason("document", item.Id, usage.Data!, item.Identity)
            : CouldNotCheck("document type", item.Id, usage.ErrorMessage);
    }

    /// <summary>The media twin of <see cref="DocumentTypeAsync"/> (#287).</summary>
    /// <param name="client">The client to check with.</param>
    /// <param name="item">The media type: its id, and its alias when known (a prune, #396).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reason, or null.</returns>
    private static async Task<string?> MediaTypeAsync(
        IMediaTypeClient client,
        DeleteTarget.Item item,
        CancellationToken ct
    )
    {
        var usage = await client.GetMediaTypeUsageAsync(item.Id, ct);
        return usage.IsSuccess
            ? TypeUsageReason("media", item.Id, usage.Data!, item.Identity)
            : CouldNotCheck("media type", item.Id, usage.ErrorMessage);
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
    /// <param name="alias">
    /// The type's alias, or null when not known. A prune knows it from the snapshot diff; a delete
    /// named the type itself on the command line.
    /// </param>
    /// <returns>The reason, naming the type by alias (when known) and id, or null.</returns>
    internal static string? TypeUsageReason(
        string item,
        Guid id,
        TypeUsage usage,
        string? alias = null
    )
    {
        // Name the type by alias as well as id when the alias is known (#396), so a prune
        // refusal says which type it means without a lookup.
        var type = alias is { Length: > 0 }
            ? $"{Capitalised(item)} type {alias} ({id})"
            : $"{Capitalised(item)} type {id}";
        var reasons = new List<string>();
        if (usage.Items > 0)
            reasons.Add(
                $"{type} has {usage.Items} {item} item(s), counting the "
                    + "recycle bin. Deleting it also deletes them."
            );
        if (usage.ComposedBy.Count > 0)
            reasons.Add(
                $"{type} is a composition of {Summarise([.. usage.ComposedBy])}. "
                    + "Deleting it removes its properties, and the values stored in them, from "
                    + "those types."
            );
        if (usage.IsElement)
            reasons.Add(
                $"{type} is an element type. Block content in other "
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
