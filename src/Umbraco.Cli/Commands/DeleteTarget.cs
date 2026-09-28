using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands;

/// <summary>
/// What a delete would remove, as <see cref="InUseGuard.ReasonAsync"/> needs to know it (#281):
/// an id-keyed item, a language (keyed by ISO code) or a static file (keyed by path).
/// </summary>
public abstract record DeleteTarget
{
    /// <summary>An id-keyed item: a type, data type, template, group or dictionary item.</summary>
    /// <param name="Kind">What the id names.</param>
    /// <param name="Id">The item id.</param>
    /// <param name="Identity">Its human key (alias, name), for messages that name it; null when not known.</param>
    public sealed record Item(EntityKind Kind, Guid Id, string? Identity = null) : DeleteTarget;

    /// <summary>A language, which has no id.</summary>
    /// <param name="IsoCode">The language's ISO code.</param>
    public sealed record Language(string IsoCode) : DeleteTarget;

    /// <summary>A partial view, stylesheet or script, or a folder of them (#292).</summary>
    /// <param name="Kind">The static-file kind.</param>
    /// <param name="Path">The file or folder path.</param>
    /// <param name="IsFolder">Whether it is a folder.</param>
    public sealed record StaticFile(StaticFileKind Kind, string Path, bool IsFolder) : DeleteTarget;
}

/// <summary>
/// The rest of a <c>schema apply --prune</c> plan, as the delete checks see it (#281): what else
/// the same run deletes or moves away, and how it rewrites templates. A child the prune also
/// deletes is expected to go, and so is a template user the prune also deletes, so
/// <see cref="InUseGuard.ReasonAsync"/> answers with the plan in mind when it is given one.
/// <para>
/// The reads a check needs are made once per plan and shared by every check (the template usage,
/// the dictionary tree, the templates as they will stand), and made only when a check asks.
/// </para>
/// </summary>
public sealed class DeletePlanContext
{
    /// <summary>The id-keyed items the plan deletes, by kind.</summary>
    private readonly ILookup<EntityKind, Guid> _deleted;

    /// <summary>The dictionary items the plan moves to another parent (updates run before deletes).</summary>
    private readonly HashSet<Guid> _movedDictionaryItems;

    /// <summary>The templates the plan updates: live id to the snapshot body it writes.</summary>
    private readonly IReadOnlyDictionary<Guid, JsonNode> _updatedTemplates;

    /// <summary>The bodies of the templates the plan creates.</summary>
    private readonly IReadOnlyList<JsonNode> _createdTemplates;

    /// <summary>The template usage read, once asked for.</summary>
    private Task<
        UmbracoResponse<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>
    >? _templateUsage;

    /// <summary>The dictionary tree read, once asked for.</summary>
    private Task<Dictionary<Guid, List<Guid>>?>? _dictionaryChildren;

    /// <summary>The templates after the plan, once asked for.</summary>
    private Task<IReadOnlyList<(string Name, string Content)>?>? _templatesAfter;

    /// <summary>Describes a plan.</summary>
    /// <param name="deleted">Every id-keyed item the plan deletes.</param>
    /// <param name="movedDictionaryItems">Every dictionary item the plan moves to another parent.</param>
    /// <param name="updatedTemplates">Every template the plan updates, by live id, with the body it writes.</param>
    /// <param name="createdTemplates">The body of every template the plan creates.</param>
    public DeletePlanContext(
        IEnumerable<(EntityKind Kind, Guid Id)> deleted,
        IEnumerable<Guid> movedDictionaryItems,
        IReadOnlyDictionary<Guid, JsonNode> updatedTemplates,
        IReadOnlyList<JsonNode> createdTemplates
    )
    {
        _deleted = deleted.ToLookup(d => d.Kind, d => d.Id);
        _movedDictionaryItems = [.. movedDictionaryItems];
        _updatedTemplates = updatedTemplates;
        _createdTemplates = createdTemplates;
    }

    /// <summary>An empty plan: nothing else goes away, and the templates are the live ones.</summary>
    /// <returns>A new empty context (each has its own read caches).</returns>
    public static DeletePlanContext Nothing() => new([], [], new Dictionary<Guid, JsonNode>(), []);

    /// <summary>Whether the plan deletes the <paramref name="kind"/> <paramref name="id"/>.</summary>
    /// <param name="kind">The item kind.</param>
    /// <param name="id">The item id.</param>
    /// <returns>True when it goes away with the plan.</returns>
    public bool Deletes(EntityKind kind, Guid id) => _deleted[kind].Contains(id);

    /// <summary>Whether the plan moves dictionary item <paramref name="id"/> to another parent.</summary>
    /// <param name="id">The dictionary item id.</param>
    /// <returns>True when it is moved away from its current parent.</returns>
    public bool MovesAway(Guid id) => _movedDictionaryItems.Contains(id);

    /// <summary>Every template's document type users, read once for the plan.</summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The usage, or the read failure.</returns>
    internal Task<
        UmbracoResponse<IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>>
    > TemplateUsageAsync(ITemplateClient client, CancellationToken ct) =>
        _templateUsage ??= client.GetTemplateUsageAsync(ct);

    /// <summary>
    /// Each live dictionary item's children, read once for the plan; null when the tree could not
    /// be read (an unknown is not a yes).
    /// </summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The children by parent id, or null.</returns>
    internal Task<Dictionary<Guid, List<Guid>>?> DictionaryChildrenAsync(
        ISchemaClient client,
        CancellationToken ct
    ) => _dictionaryChildren ??= ReadDictionaryChildrenAsync(client, ct);

    /// <summary>
    /// Every template's name and content as they will stand after the plan (#292), read once: the
    /// live templates, less the ones the plan deletes, with the snapshot content for the ones it
    /// updates, plus the ones it creates. So a template the same apply rewrites to stop using a
    /// file does not block that file's prune, and one it adds does. Null when the live templates
    /// could not be read.
    /// </summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The templates, or null.</returns>
    internal Task<IReadOnlyList<(string Name, string Content)>?> TemplatesAfterAsync(
        IUmbracoManagementClient client,
        CancellationToken ct
    ) => _templatesAfter ??= ReadTemplatesAfterAsync(client, ct);

    /// <summary>Reads the dictionary tree into each item's children.</summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The children by parent id, or null when the tree could not be read.</returns>
    private static async Task<Dictionary<Guid, List<Guid>>?> ReadDictionaryChildrenAsync(
        ISchemaClient client,
        CancellationToken ct
    )
    {
        var entries = await client.GetDictionaryEntriesAsync(ct);
        if (!entries.IsSuccess)
            return null;
        var children = new Dictionary<Guid, List<Guid>>();
        foreach (var entry in entries.Data!)
        {
            if (entry.ParentId is not { } parent)
                continue;
            if (!children.TryGetValue(parent, out var list))
                children[parent] = list = [];
            list.Add(entry.Id);
        }
        return children;
    }

    /// <summary>Reads the live templates and lays the plan's template writes over them.</summary>
    /// <param name="client">The client to read with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The templates after the plan, or null when the live ones could not be read.</returns>
    private async Task<IReadOnlyList<(string Name, string Content)>?> ReadTemplatesAfterAsync(
        IUmbracoManagementClient client,
        CancellationToken ct
    )
    {
        var ids = await client.GetTemplateIdsAsync(ct);
        if (!ids.IsSuccess)
            return null;

        var result = new List<(string, string)>();
        foreach (var id in ids.Data!.Where(i => !Deletes(EntityKind.Template, i)))
        {
            JsonNode body;
            if (_updatedTemplates.TryGetValue(id, out var desired))
                body = desired;
            else
            {
                var live = await client.GetSchemaRawAsync(EntityKind.Template, id, ct);
                if (!live.IsSuccess)
                    return null;
                body = live.Data!;
            }
            result.Add(TemplateText(body));
        }
        result.AddRange(_createdTemplates.Select(TemplateText));
        return result;
    }

    /// <summary>A template body's display name (its name, else alias) and its Razor content.</summary>
    /// <param name="body">The template body.</param>
    /// <returns>The name and content.</returns>
    private static (string Name, string Content) TemplateText(JsonNode body) =>
        ((string?)body["name"] ?? (string?)body["alias"] ?? "", (string?)body["content"] ?? "");
}
