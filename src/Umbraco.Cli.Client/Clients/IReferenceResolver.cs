namespace Umbraco.Cli.Client;

/// <summary>The kinds of item a command can name by something other than its id (#250 Phase 3).</summary>
public enum EntityKind
{
    /// <summary>A template, named by alias (then name).</summary>
    Template,

    /// <summary>A document type, named by alias.</summary>
    DocumentType,

    /// <summary>A media type, named by alias (then name).</summary>
    MediaType,

    /// <summary>A member type, named by alias.</summary>
    MemberType,

    /// <summary>A data type, named by name (data types have no alias).</summary>
    DataType,

    /// <summary>A user group, named by alias (then name).</summary>
    UserGroup,

    /// <summary>A member group, named by name.</summary>
    MemberGroup,

    /// <summary>A dictionary item, named by its key.</summary>
    DictionaryItem,
}

/// <summary>How messages and help text name each <see cref="EntityKind"/>.</summary>
public static class EntityKindText
{
    /// <summary>The kind as a noun, e.g. <c>media type</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The noun.</returns>
    public static string Noun(this EntityKind kind) => Describe(kind).Noun;

    /// <summary>What the kind is looked up by besides its id, e.g. <c>alias or name</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The key description.</returns>
    public static string KeyName(this EntityKind kind) => Describe(kind).By;

    /// <summary>The command that lists the kind, e.g. <c>media-types list</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The command, without the <c>umbraco</c> prefix.</returns>
    public static string ListCommand(this EntityKind kind) => Describe(kind).List;

    private static (string Noun, string By, string List) Describe(EntityKind kind) =>
        kind switch
        {
            EntityKind.Template => ("template", "alias or name", "templates list"),
            EntityKind.DocumentType => ("document type", "alias", "content-types list"),
            EntityKind.MediaType => ("media type", "alias or name", "media-types list"),
            EntityKind.MemberType => ("member type", "alias", "member-types list"),
            EntityKind.DataType => ("data type", "name", "data-types list"),
            EntityKind.UserGroup => ("user group", "alias or name", "user-groups list"),
            EntityKind.MemberGroup => ("member group", "name", "member-groups list"),
            EntityKind.DictionaryItem => ("dictionary item", "key", "dictionary list"),
            _ => (kind.ToString(), "reference", "--help"),
        };
}

/// <summary>
/// Turns what a user types for an item - its id, alias, name or key - into the item's id, the same
/// way for every kind (#250 Phase 3). Before this each noun had its own lookup, most commands took
/// only a GUID, and several lookups used the item search, which matches <b>names</b>, so an alias
/// that differs from its name ("blogPost" vs "Blog Post") was never found (#206).
/// <para>
/// The rules, for every kind: a value that parses as a GUID is that id, with no request. Otherwise
/// it is matched, ignoring case, against the kind's alias first and then its name (see
/// <see cref="EntityKind"/> for which each kind has). A value that matches no item is a 404 naming
/// the kind and the list command to look it up with; a <b>name</b> that matches more than one item
/// is refused (409) with every match's id, because picking one would act on an item the caller did
/// not choose.
/// </para>
/// </summary>
public interface IReferenceResolver
{
    /// <summary>Resolves <paramref name="reference"/> to the id of a <paramref name="kind"/> item.</summary>
    /// <param name="kind">What kind of item the reference names.</param>
    /// <param name="reference">An id, alias, name or key, as the kind allows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id, or a 404 (no match) / 409 (ambiguous name) failure.</returns>
    Task<UmbracoResponse<Guid>> ResolveIdAsync(
        EntityKind kind,
        string reference,
        CancellationToken ct = default
    );
}

/// <summary>Composes <see cref="IReferenceResolver"/> with the id-taking client calls.</summary>
public static class ReferenceResolverExtensions
{
    /// <summary>Resolves a reference, then runs <paramref name="call"/> with its id.</summary>
    /// <typeparam name="T">The call's payload type.</typeparam>
    /// <param name="resolver">The resolver (the management client).</param>
    /// <param name="kind">What kind of item the reference names.</param>
    /// <param name="reference">An id, alias, name or key.</param>
    /// <param name="call">The id-taking call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The call's response, or the resolution failure.</returns>
    public static async Task<UmbracoResponse<T>> WithResolvedAsync<T>(
        this IReferenceResolver resolver,
        EntityKind kind,
        string reference,
        Func<Guid, Task<UmbracoResponse<T>>> call,
        CancellationToken ct = default
    )
    {
        var id = await resolver.ResolveIdAsync(kind, reference, ct);
        return id.IsSuccess ? await call(id.Data) : UmbracoResponse<T>.FailureFrom(id);
    }

    /// <summary>
    /// Resolves an optional reference, such as a <c>--parent</c>: null stays null (the root),
    /// anything else must resolve.
    /// </summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="kind">What kind of item the reference names.</param>
    /// <param name="reference">An id, alias, name or key; null for none.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The id or null, or the resolution failure.</returns>
    public static async Task<UmbracoResponse<Guid?>> ResolveOptionalAsync(
        this IReferenceResolver resolver,
        EntityKind kind,
        string? reference,
        CancellationToken ct = default
    )
    {
        if (reference is null)
            return UmbracoResponse<Guid?>.Success(null);
        var id = await resolver.ResolveIdAsync(kind, reference, ct);
        return id.IsSuccess
            ? UmbracoResponse<Guid?>.Success(id.Data)
            : UmbracoResponse<Guid?>.FailureFrom(id);
    }

    /// <summary>Resolves several references, stopping at the first that fails.</summary>
    /// <param name="resolver">The resolver.</param>
    /// <param name="kind">What kind of item the references name.</param>
    /// <param name="references">Ids, aliases, names or keys.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ids in the order given, or the first failure.</returns>
    public static async Task<UmbracoResponse<IReadOnlyList<Guid>>> ResolveIdsAsync(
        this IReferenceResolver resolver,
        EntityKind kind,
        IEnumerable<string> references,
        CancellationToken ct = default
    )
    {
        var ids = new List<Guid>();
        foreach (var reference in references)
        {
            var id = await resolver.ResolveIdAsync(kind, reference, ct);
            if (!id.IsSuccess)
                return UmbracoResponse<IReadOnlyList<Guid>>.FailureFrom(id);
            ids.Add(id.Data);
        }
        return UmbracoResponse<IReadOnlyList<Guid>>.Success(ids);
    }
}
