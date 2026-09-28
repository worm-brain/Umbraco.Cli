using System.Text.Json.Nodes;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Schema;

/// <summary>
/// Everything the schema pipeline needs to know about one snapshot kind (#273): its tag, where it
/// lives in the snapshot and the diff, how it is matched, read, ordered and written. The pipeline
/// (diff engine, applier, exporter, snapshot parser) loops over <see cref="SchemaKinds.All"/>
/// rather than naming kinds, so a kind is declared once, here.
/// </summary>
public sealed record SchemaKindSpec
{
    /// <summary>The kind tag used in diff rows and apply actions, e.g. <c>documentType</c>.</summary>
    public required string Tag { get; init; }

    /// <summary>The snapshot JSON member that holds the kind's entries, e.g. <c>documentTypes</c>.</summary>
    public required string Member { get; init; }

    /// <summary>
    /// The body field holding the human key the diff falls back to when ids do not match:
    /// <c>alias</c>, <c>name</c>, <c>isoCode</c> or (static files) <c>path</c>.
    /// </summary>
    public required string KeyField { get; init; }

    /// <summary>
    /// When this kind is created and deleted relative to the other entity kinds: creates run in
    /// ascending stage, deletes in descending stage (so the two cannot drift apart). Static files
    /// (<see cref="File"/> set) are a separate track (#292): created before every entity kind and
    /// deleted after every one, in ascending stage both times.
    /// </summary>
    public required int ApplyStage { get; init; }

    /// <summary>The client entity kind of an id-keyed kind; null for languages and static files, which have no id.</summary>
    public EntityKind? Entity { get; init; }

    /// <summary>The static-file kind, for the three file kinds (#292); null for every other kind.</summary>
    public StaticFileKind? File { get; init; }

    /// <summary>Reads the kind's section of a snapshot; null when the section is absent (not managed).</summary>
    public required Func<SchemaSnapshot, List<JsonNode>?> Section { get; init; }

    /// <summary>Sets the kind's section of a snapshot (null leaves it absent).</summary>
    public required Action<SchemaSnapshot, List<JsonNode>?> SetSection { get; init; }

    /// <summary>Reads the kind's part of a diff.</summary>
    public required Func<SchemaDiff, SchemaKindDiff> Diff { get; init; }

    /// <summary>Returns a copy of a diff with the kind's part replaced.</summary>
    public required Func<SchemaDiff, SchemaKindDiff, SchemaDiff> WithDiff { get; init; }

    /// <summary>Reads every entity of the kind from a live instance, as snapshot entries, or the first failure.</summary>
    public required Func<
        IUmbracoManagementClient,
        CancellationToken,
        Task<UmbracoResponse<List<JsonNode>>>
    > Export { get; init; }

    /// <summary>
    /// Why a live entity can never be deleted (the default language, a built-in user group), or
    /// null. An unmatched live entity with a reason is skipped with it instead of pruned.
    /// </summary>
    public Func<JsonNode, string?>? Undeletable { get; init; }

    /// <summary>
    /// Rewrites a desired body once matching is done, given the snapshot-to-live id pairs, for a
    /// same-kind reference that must name the target's ids (a dictionary item's parent). Null
    /// leaves bodies as they are.
    /// </summary>
    public Func<JsonNode, IReadOnlyDictionary<Guid, Guid>, JsonNode>? Rewrite { get; init; }

    /// <summary>
    /// Where the kind's bodies reference other schema items by id, which a hand-written snapshot
    /// may give by name instead (#198; see <see cref="SchemaReferences"/>).
    /// </summary>
    public IReadOnlyList<SchemaReference> References { get; init; } = [];

    /// <summary>
    /// Whether the kind's bodies carry <c>properties</c> and <c>containers</c> (the three types),
    /// whose ids a hand-written snapshot may leave out (#198).
    /// </summary>
    public bool HasProperties { get; init; }

    /// <summary>Orders the kind's creates among themselves (referenced entities first).</summary>
    public Func<
        IReadOnlyList<SchemaEntityChange>,
        IEnumerable<SchemaEntityChange>
    > CreateOrder { get; init; } = SchemaOrder.CreatesByIdReference;

    /// <summary>Orders the kind's deletes among themselves (referrers first).</summary>
    public Func<
        IReadOnlyList<SchemaEntityChange>,
        IEnumerable<SchemaEntityChange>
    > DeleteOrder { get; init; } = SchemaOrder.AsEnumerated;

    /// <summary>Creates an added entity.</summary>
    public required SchemaWrite Create { get; init; }

    /// <summary>Updates a changed entity.</summary>
    public required SchemaWrite Update { get; init; }

    /// <summary>Deletes a removed (pruned) entity.</summary>
    public required SchemaWrite Delete { get; init; }

    /// <summary>
    /// What a prune's delete of a removed entity removes, for the delete-safety check
    /// (<see cref="InUseGuard.ReasonAsync"/>, #281).
    /// </summary>
    public required Func<SchemaEntityChange, DeleteTarget> Target { get; init; }
}

/// <summary>
/// The schema snapshot kinds (#68, extended by #186, #227 and #292): the tag constants and the one
/// kind table, <see cref="All"/>, that every part of the pipeline loops over (#273).
/// </summary>
public static class SchemaKinds
{
    /// <summary>Document type kind tag.</summary>
    public const string DocumentType = "documentType";

    /// <summary>Media type kind tag (#186).</summary>
    public const string MediaType = "mediaType";

    /// <summary>Member type kind tag (#186).</summary>
    public const string MemberType = "memberType";

    /// <summary>Data type kind tag.</summary>
    public const string DataType = "dataType";

    /// <summary>Template kind tag.</summary>
    public const string Template = "template";

    /// <summary>Language kind tag (#227). Languages are keyed by ISO code and have no id.</summary>
    public const string Language = "language";

    /// <summary>Dictionary item kind tag (#227), keyed by the item's key (its name).</summary>
    public const string DictionaryItem = "dictionaryItem";

    /// <summary>Member group kind tag (#227), keyed by name.</summary>
    public const string MemberGroup = "memberGroup";

    /// <summary>User group kind tag (#227), keyed by alias.</summary>
    public const string UserGroup = "userGroup";

    /// <summary>Partial view kind tag (#292), keyed by path. Covers its folders too.</summary>
    public const string PartialView = "partialView";

    /// <summary>Stylesheet kind tag (#292), keyed by path. Covers its folders too.</summary>
    public const string Stylesheet = "stylesheet";

    /// <summary>Script kind tag (#292), keyed by path. Covers its folders too.</summary>
    public const string Script = "script";

    /// <summary>
    /// Every snapshot kind, in snapshot order: the order of the snapshot file's sections and of the
    /// <c>schema diff</c> rows. Apply order is each kind's <see cref="SchemaKindSpec.ApplyStage"/>:
    /// static files, then languages -> dictionary items -> member groups -> data types -> templates
    /// -> media types -> member types -> document types -> user groups (dictionary translations
    /// name languages, every type's properties reference data types, a document type's
    /// <c>allowedTemplates</c> reference templates, and a user group's property permissions
    /// reference document types). Prune runs the entity kinds in reverse, then the static files,
    /// after the templates that use them.
    /// </summary>
    public static readonly IReadOnlyList<SchemaKindSpec> All =
    [
        TypeKind(
            DocumentType,
            "documentTypes",
            EntityKind.DocumentType,
            stage: 10,
            s => s.DocumentTypes,
            (s, v) => s.DocumentTypes = v,
            d => d.DocumentTypes,
            (d, k) => d with { DocumentTypes = k },
            (c, ct) => c.GetDocumentTypeIdsAsync(ct),
            (c, id, ct) => c.DeleteDocumentTypeAsync(id, ct)
        ) with
        {
            HasProperties = true,
            References =
            [
                .. TypeReferences("documentType", EntityKind.DocumentType),
                new("allowedDocumentTypes", "documentType", EntityKind.DocumentType),
                new("allowedTemplates", null, EntityKind.Template),
                new(null, "defaultTemplate", EntityKind.Template),
            ],
        },
        // Media and member types carry an alias, exactly as document types do (#186).
        TypeKind(
            MediaType,
            "mediaTypes",
            EntityKind.MediaType,
            stage: 8,
            s => s.MediaTypes,
            (s, v) => s.MediaTypes = v,
            d => d.MediaTypes,
            (d, k) => d with { MediaTypes = k },
            (c, ct) => c.GetMediaTypeIdsAsync(ct),
            (c, id, ct) => c.DeleteMediaTypeAsync(id, ct)
        ) with
        {
            HasProperties = true,
            References =
            [
                .. TypeReferences("mediaType", EntityKind.MediaType),
                new("allowedMediaTypes", "mediaType", EntityKind.MediaType),
            ],
        },
        TypeKind(
            MemberType,
            "memberTypes",
            EntityKind.MemberType,
            stage: 9,
            s => s.MemberTypes,
            (s, v) => s.MemberTypes = v,
            d => d.MemberTypes,
            (d, k) => d with { MemberTypes = k },
            (c, ct) => c.GetMemberTypeIdsAsync(ct),
            (c, id, ct) => c.DeleteMemberTypeAsync(id, ct)
        ) with
        {
            HasProperties = true,
            References = TypeReferences("memberType", EntityKind.MemberType),
        },
        // Data types have no alias: they match on name.
        TypeKind(
            DataType,
            "dataTypes",
            EntityKind.DataType,
            stage: 6,
            s => s.DataTypes,
            (s, v) => s.DataTypes = v,
            d => d.DataTypes,
            (d, k) => d with { DataTypes = k },
            (c, ct) => c.GetDataTypeIdsAsync(ct),
            (c, id, ct) => c.DeleteDataTypeAsync(id, ct)
        ) with
        {
            KeyField = "name",
        },
        TypeKind(
            Template,
            "templates",
            EntityKind.Template,
            stage: 7,
            s => s.Templates,
            (s, v) => s.Templates = v,
            d => d.Templates,
            (d, k) => d with { Templates = k },
            (c, ct) => c.GetTemplateIdsAsync(ct),
            (c, id, ct) => c.DeleteTemplateAsync(id, ct)
        ) with
        {
            References = [new(null, "masterTemplate", EntityKind.Template)],
        },
        // #227. Languages have no id at all, so they match on the ISO code alone and are written
        // by it.
        new SchemaKindSpec
        {
            Tag = Language,
            Member = "languages",
            KeyField = "isoCode",
            ApplyStage = 3,
            Section = s => s.Languages,
            SetSection = (s, v) => s.Languages = v,
            Diff = d => d.Languages,
            WithDiff = (d, k) => d with { Languages = k },
            Export = async (c, ct) =>
            {
                // Languages come whole from the list, not per id like the rest.
                var languages = await c.GetLanguagesRawAsync(ct);
                return languages.IsSuccess
                    ? UmbracoResponse<List<JsonNode>>.Success([.. languages.Data!])
                    : UmbracoResponse<List<JsonNode>>.Failure(
                        languages.StatusCode,
                        languages.ErrorMessage!
                    );
            },
            Undeletable = live =>
                Flag(live, "isDefault", true)
                    ? "The default language cannot be deleted; make another language the default first."
                    : null,
            CreateOrder = SchemaOrder.LanguageCreates,
            DeleteOrder = SchemaOrder.LanguageDeletes,
            Create = (c, change, ct) => c.CreateLanguageRawAsync(change.DesiredBody!, ct),
            Update = (c, change, ct) =>
                c.UpdateLanguageRawAsync(change.Identity, change.DesiredBody!, ct),
            Delete = (c, change, ct) => c.DeleteLanguageAsync(change.Identity, ct),
            Target = change => new DeleteTarget.Language(change.Identity),
        },
        // A dictionary item's parent is another dictionary item, which usually has a different id
        // on each instance: the parent is translated to the target's id before comparing, so a
        // name-matched tree compares equal and apply writes the target's ids.
        new SchemaKindSpec
        {
            Tag = DictionaryItem,
            Member = "dictionaryItems",
            KeyField = "name",
            ApplyStage = 4,
            Entity = EntityKind.DictionaryItem,
            Section = s => s.DictionaryItems,
            SetSection = (s, v) => s.DictionaryItems = v,
            Diff = d => d.DictionaryItems,
            WithDiff = (d, k) => d with { DictionaryItems = k },
            Export = SchemaExporter.CollectDictionaryAsync,
            Rewrite = SchemaBodies.WithLiveParent,
            References = [new(null, "parent", EntityKind.DictionaryItem)],
            DeleteOrder = SchemaOrder.DictionaryDeletes,
            Create = SchemaWrites.Create(EntityKind.DictionaryItem),
            Update = SchemaWrites.UpdateDictionaryItemAsync,
            Delete = SchemaWrites.DeleteById((c, id, ct) => c.DeleteDictionaryItemAsync(id, ct)),
            Target = ItemTarget(EntityKind.DictionaryItem),
        },
        TypeKind(
            MemberGroup,
            "memberGroups",
            EntityKind.MemberGroup,
            stage: 5,
            s => s.MemberGroups,
            (s, v) => s.MemberGroups = v,
            d => d.MemberGroups,
            (d, k) => d with { MemberGroups = k },
            (c, ct) => c.GetMemberGroupIdsAsync(ct),
            (c, id, ct) => c.DeleteMemberGroupAsync(id, ct)
        ) with
        {
            KeyField = "name",
        },
        TypeKind(
            UserGroup,
            "userGroups",
            EntityKind.UserGroup,
            stage: 11,
            s => s.UserGroups,
            (s, v) => s.UserGroups = v,
            d => d.UserGroups,
            (d, k) => d with { UserGroups = k },
            (c, ct) => c.GetUserGroupIdsAsync(ct),
            (c, id, ct) => c.DeleteUserGroupAsync(id, ct)
        ) with
        {
            // A property-value permission names the document type it applies to.
            References = [new("permissions", "documentType", EntityKind.DocumentType)],
            // Start nodes and per-document permissions name content on one instance only.
            Export = async (c, ct) =>
            {
                var groups = await SchemaExporter.CollectAsync(
                    () => c.GetUserGroupIdsAsync(ct),
                    id => c.GetSchemaRawAsync(EntityKind.UserGroup, id, ct)
                );
                return groups.IsSuccess
                    ? UmbracoResponse<List<JsonNode>>.Success([
                        .. groups.Data!.Select(SchemaBodies.PortableUserGroup),
                    ])
                    : groups;
            },
            Undeletable = live =>
                Flag(live, "isDeletable", false)
                    ? "Umbraco does not allow this user group to be deleted."
                    : null,
            Update = SchemaWrites.UpdateUserGroupAsync,
        },
        // #292: the files the templates render travel with them, so a promoted site does not
        // answer every page with a 500.
        FileKind(
            PartialView,
            "partialViews",
            StaticFileKind.PartialView,
            stage: 0,
            s => s.PartialViews,
            (s, v) => s.PartialViews = v,
            d => d.PartialViews,
            (d, k) => d with { PartialViews = k }
        ),
        FileKind(
            Stylesheet,
            "stylesheets",
            StaticFileKind.Stylesheet,
            stage: 1,
            s => s.Stylesheets,
            (s, v) => s.Stylesheets = v,
            d => d.Stylesheets,
            (d, k) => d with { Stylesheets = k }
        ),
        FileKind(
            Script,
            "scripts",
            StaticFileKind.Script,
            stage: 2,
            s => s.Scripts,
            (s, v) => s.Scripts = v,
            d => d.Scripts,
            (d, k) => d with { Scripts = k }
        ),
    ];

    /// <summary>The spec for a kind tag.</summary>
    /// <param name="tag">A kind tag, e.g. <see cref="DocumentType"/>.</param>
    /// <returns>The spec.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The tag is not a schema kind.</exception>
    public static SchemaKindSpec Of(string tag) =>
        All.FirstOrDefault(k => k.Tag == tag)
        ?? throw new ArgumentOutOfRangeException(nameof(tag), tag, "Not a schema kind.");

    /// <summary>The kinds in create order: static files, then the entity kinds by ascending stage.</summary>
    public static IEnumerable<SchemaKindSpec> CreateOrder =>
        All.Where(k => k.File is not null)
            .OrderBy(k => k.ApplyStage)
            .Concat(All.Where(k => k.File is null).OrderBy(k => k.ApplyStage));

    /// <summary>
    /// The kinds in delete order: the entity kinds by descending stage (the reverse of their
    /// create order), then the static files, after the templates that use them.
    /// </summary>
    public static IEnumerable<SchemaKindSpec> DeleteOrder =>
        All.Where(k => k.File is null)
            .OrderByDescending(k => k.ApplyStage)
            .Concat(All.Where(k => k.File is not null).OrderBy(k => k.ApplyStage));

    /// <summary>
    /// The spec of an id-keyed kind that is read per id and written through the generic raw
    /// create and full-replace update: the types, data types, templates and groups. Keyed by
    /// <c>alias</c>; a kind keyed otherwise overrides <see cref="SchemaKindSpec.KeyField"/>.
    /// </summary>
    /// <param name="tag">The kind tag.</param>
    /// <param name="member">The snapshot member.</param>
    /// <param name="entity">The client entity kind.</param>
    /// <param name="stage">The apply stage.</param>
    /// <param name="section">Reads the snapshot section.</param>
    /// <param name="setSection">Sets the snapshot section.</param>
    /// <param name="diff">Reads the kind's diff.</param>
    /// <param name="withDiff">Replaces the kind's diff.</param>
    /// <param name="listIds">Enumerates every live id of the kind.</param>
    /// <param name="delete">The client's delete call for the kind.</param>
    /// <returns>The spec.</returns>
    private static SchemaKindSpec TypeKind(
        string tag,
        string member,
        EntityKind entity,
        int stage,
        Func<SchemaSnapshot, List<JsonNode>?> section,
        Action<SchemaSnapshot, List<JsonNode>?> setSection,
        Func<SchemaDiff, SchemaKindDiff> diff,
        Func<SchemaDiff, SchemaKindDiff, SchemaDiff> withDiff,
        Func<
            IUmbracoManagementClient,
            CancellationToken,
            Task<UmbracoResponse<IReadOnlyList<Guid>>>
        > listIds,
        Func<IUmbracoManagementClient, Guid, CancellationToken, Task<UmbracoResponse<Empty>>> delete
    ) =>
        new()
        {
            Tag = tag,
            Member = member,
            KeyField = "alias",
            ApplyStage = stage,
            Entity = entity,
            Section = section,
            SetSection = setSection,
            Diff = diff,
            WithDiff = withDiff,
            Export = (c, ct) =>
                SchemaExporter.CollectAsync(
                    () => listIds(c, ct),
                    id => c.GetSchemaRawAsync(entity, id, ct)
                ),
            Create = SchemaWrites.Create(entity),
            Update = SchemaWrites.Update(entity),
            Delete = SchemaWrites.DeleteById(delete),
            Target = ItemTarget(entity),
        };

    /// <summary>
    /// The spec of a static-file kind (#292): keyed by path, with no id, read by walking the tree,
    /// and written by path. Its section is null when absent, meaning the snapshot does not manage
    /// the kind.
    /// </summary>
    /// <param name="tag">The kind tag.</param>
    /// <param name="member">The snapshot member.</param>
    /// <param name="kind">The static-file kind.</param>
    /// <param name="stage">The order among the file kinds.</param>
    /// <param name="section">Reads the snapshot section.</param>
    /// <param name="setSection">Sets the snapshot section.</param>
    /// <param name="diff">Reads the kind's diff.</param>
    /// <param name="withDiff">Replaces the kind's diff.</param>
    /// <returns>The spec.</returns>
    private static SchemaKindSpec FileKind(
        string tag,
        string member,
        StaticFileKind kind,
        int stage,
        Func<SchemaSnapshot, List<JsonNode>?> section,
        Action<SchemaSnapshot, List<JsonNode>?> setSection,
        Func<SchemaDiff, SchemaKindDiff> diff,
        Func<SchemaDiff, SchemaKindDiff, SchemaDiff> withDiff
    ) =>
        new()
        {
            Tag = tag,
            Member = member,
            KeyField = "path",
            ApplyStage = stage,
            File = kind,
            Section = section,
            SetSection = setSection,
            Diff = diff,
            WithDiff = withDiff,
            Export = (c, ct) => SchemaStaticFiles.CollectAsync(c, kind, ct),
            CreateOrder = SchemaOrder.FileCreates,
            DeleteOrder = SchemaOrder.FileDeletes,
            Create = SchemaWrites.File(kind, "create"),
            Update = SchemaWrites.File(kind, "update"),
            Delete = SchemaWrites.File(kind, "delete"),
            Target = change => new DeleteTarget.StaticFile(
                kind,
                change.Identity,
                SchemaStaticFiles.IsFolder(change.CurrentBody)
            ),
        };

    /// <summary>
    /// The references every type body carries (#198): each property's data type, the list view's
    /// data type (<c>collection</c>), and its compositions, which name types of its own kind.
    /// </summary>
    /// <param name="self">The body field a composition names its type by (<c>documentType</c>, ...).</param>
    /// <param name="kind">The type's own kind.</param>
    /// <returns>The references.</returns>
    private static SchemaReference[] TypeReferences(string self, EntityKind kind) =>
        [
            new("properties", "dataType", EntityKind.DataType),
            new(null, "collection", EntityKind.DataType),
            new("compositions", self, kind),
        ];

    /// <summary>The delete target of a removed id-keyed entity: its live id, named by its identity.</summary>
    /// <param name="entity">The client entity kind.</param>
    /// <returns>The target builder.</returns>
    private static Func<SchemaEntityChange, DeleteTarget> ItemTarget(EntityKind entity) =>
        change => new DeleteTarget.Item(entity, change.CurrentId!.Value, change.Identity);

    /// <summary>Whether <paramref name="body"/>[<paramref name="field"/>] is the boolean <paramref name="value"/>.</summary>
    /// <param name="body">The entity body.</param>
    /// <param name="field">The boolean field.</param>
    /// <param name="value">The value to test for.</param>
    /// <returns>True when the field is present and equals <paramref name="value"/>.</returns>
    private static bool Flag(JsonNode body, string field, bool value) =>
        body[field] is JsonValue v && v.TryGetValue<bool>(out var b) && b == value;
}
