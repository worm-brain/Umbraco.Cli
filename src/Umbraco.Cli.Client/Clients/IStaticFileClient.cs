namespace Umbraco.Cli.Client;

/// <summary>
/// Umbraco's three static-file resources, which share an identical path-addressed API shape and
/// differ only by generated type names (issue #105). Passed to <see cref="IStaticFileClient"/> so
/// one implementation covers all three.
/// </summary>
public enum StaticFileKind
{
    /// <summary>JavaScript files under <c>/scripts</c> (URL slug <c>script</c>).</summary>
    Script,

    /// <summary>CSS files (URL slug <c>stylesheet</c>).</summary>
    Stylesheet,

    /// <summary>Razor partial views (URL slug <c>partial-view</c>).</summary>
    PartialView,
}

/// <summary>
/// Read/write access to Umbraco's static-file resources - scripts, stylesheets, and partial views
/// (issue #105). Unlike the GUID-keyed resources these are addressed by <b>file path</b>; the path
/// is passed raw (the generated client percent-encodes it). One interface serves all three kinds
/// via the <see cref="StaticFileKind"/> argument.
/// </summary>
public interface IStaticFileClient
{
    /// <summary>
    /// Lists a kind's files from the file-system tree: the root when <paramref name="parentPath"/>
    /// is null, otherwise the children of that folder path.
    /// </summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="parentPath">Folder path to list children of; null for the tree root.</param>
    /// <param name="skip">Number of items to skip (paging).</param>
    /// <param name="take">Maximum number of items to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged list of files/folders mapped to <see cref="StaticFileTreeItem"/>.</returns>
    Task<UmbracoResponse<PagedResponse<StaticFileTreeItem>>> GetStaticFilesAsync(
        StaticFileKind kind,
        string? parentPath = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    );

    /// <summary>Gets a single file by path, including its full content.</summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="path">The file path (raw, unencoded).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The file mapped to <see cref="StaticFileResponse"/>.</returns>
    Task<UmbracoResponse<StaticFileResponse>> GetStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    );

    /// <summary>Creates a file in a kind's tree.</summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="request">The file to create (name, optional parent folder, content).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created file echoed back, or a mapped failure.</returns>
    Task<UmbracoResponse<StaticFileResponse>> CreateStaticFileAsync(
        StaticFileKind kind,
        CreateStaticFileRequest request,
        CancellationToken ct = default
    );

    /// <summary>Replaces a file's content by path (the only updatable field; rename is separate).</summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="path">The file path (raw, unencoded).</param>
    /// <param name="request">The new content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> UpdateStaticFileAsync(
        StaticFileKind kind,
        string path,
        UpdateStaticFileRequest request,
        CancellationToken ct = default
    );

    /// <summary>Deletes a file by path.</summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="path">The file path (raw, unencoded).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a folder in a kind's tree (#238) and returns it as the instance holds it.
    /// </summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="name">The folder name (one path segment).</param>
    /// <param name="parentPath">The parent folder path, with or without slashes; null for the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created folder, or a mapped failure.</returns>
    Task<UmbracoResponse<StaticFileFolderResponse>> CreateStaticFileFolderAsync(
        StaticFileKind kind,
        string name,
        string? parentPath,
        CancellationToken ct = default
    );

    /// <summary>Deletes a folder by path (#238). Umbraco refuses a folder that is not empty.</summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="path">The folder path (raw, unencoded).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    Task<UmbracoResponse<Empty>> DeleteStaticFileFolderAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    );
}
