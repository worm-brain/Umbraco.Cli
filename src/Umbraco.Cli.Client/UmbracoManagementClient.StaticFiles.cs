using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Static-file resources (scripts, stylesheets, partial views) on
/// <see cref="UmbracoManagementClient"/> (issue #105). The three share an identical path-addressed
/// API shape and differ only by generated type names, so the per-kind specifics are collected once
/// into a <see cref="StaticFileOps"/> dispatch map and each public method is a thin wrapper over it
/// (rather than repeating a <c>switch (kind)</c> in every method). Paths are passed to the
/// generated string indexers raw - Kiota percent-encodes them (slashes included) on the wire.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// The five per-kind operations. Each closes over the right generated request builder and maps
    /// to the shared command-facing records; the delegates do the raw call and the public methods
    /// add the <see cref="GuardedApiAsync{T}"/> envelope/guard once.
    /// </summary>
    private sealed record StaticFileOps(
        Func<string?, int, int, CancellationToken, Task<PagedResponse<StaticFileTreeItem>>> List,
        Func<string, CancellationToken, Task<StaticFileResponse>> Get,
        Func<CreateStaticFileRequest, CancellationToken, Task<StaticFileResponse>> Create,
        Func<string, UpdateStaticFileRequest, CancellationToken, Task<Empty>> Update,
        Func<string, CancellationToken, Task<Empty>> Delete
    );

    /// <summary>Lazily-built per-kind dispatch map (closes over the generated client).</summary>
    private Dictionary<StaticFileKind, StaticFileOps>? _staticFileOps;

    /// <summary>The per-kind dispatch map, built on first use.</summary>
    private Dictionary<StaticFileKind, StaticFileOps> StaticFileOpsMap =>
        _staticFileOps ??= BuildStaticFileOps();

    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<StaticFileTreeItem>>> GetStaticFilesAsync(
        StaticFileKind kind,
        string? parentPath = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].List(parentPath, skip, take, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<StaticFileResponse>> GetStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].Get(path, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<StaticFileResponse>> CreateStaticFileAsync(
        StaticFileKind kind,
        CreateStaticFileRequest request,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].Create(request, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateStaticFileAsync(
        StaticFileKind kind,
        string path,
        UpdateStaticFileRequest request,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].Update(path, request, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].Delete(path, ct));

    /// <summary>Builds the per-kind dispatch map. The one place the three resources' sameness lives.</summary>
    /// <returns>An operations record per <see cref="StaticFileKind"/>.</returns>
    private Dictionary<StaticFileKind, StaticFileOps> BuildStaticFileOps()
    {
        var api = _api.Umbraco.Management.Api.V1;
        return new Dictionary<StaticFileKind, StaticFileOps>
        {
            [StaticFileKind.Script] = new StaticFileOps(
                List: async (parent, skip, take, ct) =>
                    MapTree(
                        parent is null
                            ? await api.Tree.Script.Root.GetAsync(
                                c => Paged(c.QueryParameters, skip, take),
                                ct
                            )
                            : await api.Tree.Script.Children.GetAsync(
                                c => PagedChildren(c.QueryParameters, parent, skip, take),
                                ct
                            )
                    ),
                Get: async (path, ct) =>
                    Map(await api.Script[path].GetAsync(cancellationToken: ct)),
                Create: async (req, ct) =>
                {
                    await api.Script.PostAsync(
                        new Gen.CreateScriptRequestModel
                        {
                            Name = req.Name,
                            Content = req.Content,
                            Parent = Folder(req.ParentPath),
                        },
                        cancellationToken: ct
                    );
                    return Echo(req);
                },
                Update: async (path, req, ct) =>
                {
                    await api.Script[path]
                        .PutAsync(
                            new Gen.UpdateScriptRequestModel { Content = req.Content },
                            cancellationToken: ct
                        );
                    return Empty.Value;
                },
                Delete: async (path, ct) =>
                {
                    await api.Script[path].DeleteAsync(cancellationToken: ct);
                    return Empty.Value;
                }
            ),
            [StaticFileKind.Stylesheet] = new StaticFileOps(
                List: async (parent, skip, take, ct) =>
                    MapTree(
                        parent is null
                            ? await api.Tree.Stylesheet.Root.GetAsync(
                                c => Paged(c.QueryParameters, skip, take),
                                ct
                            )
                            : await api.Tree.Stylesheet.Children.GetAsync(
                                c => PagedChildren(c.QueryParameters, parent, skip, take),
                                ct
                            )
                    ),
                Get: async (path, ct) =>
                    Map(await api.Stylesheet[path].GetAsync(cancellationToken: ct)),
                Create: async (req, ct) =>
                {
                    await api.Stylesheet.PostAsync(
                        new Gen.CreateStylesheetRequestModel
                        {
                            Name = req.Name,
                            Content = req.Content,
                            Parent = Folder(req.ParentPath),
                        },
                        cancellationToken: ct
                    );
                    return Echo(req);
                },
                Update: async (path, req, ct) =>
                {
                    await api.Stylesheet[path]
                        .PutAsync(
                            new Gen.UpdateStylesheetRequestModel { Content = req.Content },
                            cancellationToken: ct
                        );
                    return Empty.Value;
                },
                Delete: async (path, ct) =>
                {
                    await api.Stylesheet[path].DeleteAsync(cancellationToken: ct);
                    return Empty.Value;
                }
            ),
            [StaticFileKind.PartialView] = new StaticFileOps(
                List: async (parent, skip, take, ct) =>
                    MapTree(
                        parent is null
                            ? await api.Tree.PartialView.Root.GetAsync(
                                c => Paged(c.QueryParameters, skip, take),
                                ct
                            )
                            : await api.Tree.PartialView.Children.GetAsync(
                                c => PagedChildren(c.QueryParameters, parent, skip, take),
                                ct
                            )
                    ),
                Get: async (path, ct) =>
                    Map(await api.PartialView[path].GetAsync(cancellationToken: ct)),
                Create: async (req, ct) =>
                {
                    await api.PartialView.PostAsync(
                        new Gen.CreatePartialViewRequestModel
                        {
                            Name = req.Name,
                            Content = req.Content,
                            Parent = Folder(req.ParentPath),
                        },
                        cancellationToken: ct
                    );
                    return Echo(req);
                },
                Update: async (path, req, ct) =>
                {
                    await api.PartialView[path]
                        .PutAsync(
                            new Gen.UpdatePartialViewRequestModel { Content = req.Content },
                            cancellationToken: ct
                        );
                    return Empty.Value;
                },
                Delete: async (path, ct) =>
                {
                    await api.PartialView[path].DeleteAsync(cancellationToken: ct);
                    return Empty.Value;
                }
            ),
        };
    }

    /// <summary>Sets skip/take on a tree-root query (the query types differ per builder but share these names).</summary>
    private static void Paged(dynamic queryParameters, int skip, int take)
    {
        queryParameters.Skip = skip;
        queryParameters.Take = take;
    }

    /// <summary>Sets parentPath/skip/take on a tree-children query.</summary>
    private static void PagedChildren(
        dynamic queryParameters,
        string parentPath,
        int skip,
        int take
    )
    {
        queryParameters.ParentPath = parentPath;
        queryParameters.Skip = skip;
        queryParameters.Take = take;
    }

    /// <summary>The parent-folder reference for a create, or null at the tree root.</summary>
    private static Gen.FileSystemFolderModel? Folder(string? parentPath) =>
        parentPath is null ? null : new Gen.FileSystemFolderModel { Path = parentPath };

    /// <summary>
    /// Best-effort echo of a just-created file: the create response is empty, so the returned path
    /// is synthesised client-side as <c>parent/name</c> (Umbraco joins with a forward slash). This
    /// is advisory - if the server sanitises the name the real path may differ; a follow-up
    /// <see cref="GetStaticFileAsync"/> returns the authoritative record.
    /// </summary>
    private static StaticFileResponse Echo(CreateStaticFileRequest req) =>
        new()
        {
            Path = req.ParentPath is null ? req.Name : $"{req.ParentPath.TrimEnd('/')}/{req.Name}",
            Name = req.Name,
            ParentPath = req.ParentPath,
            Content = req.Content,
        };

    /// <summary>Maps a shared file-system tree page onto command-facing tree items.</summary>
    private static PagedResponse<StaticFileTreeItem> MapTree(
        Gen.PagedFileSystemTreeItemPresentationModel? paged
    ) =>
        new()
        {
            Total = (int)(paged?.Total ?? 0),
            Items = (paged?.Items ?? [])
                .Select(i => new StaticFileTreeItem
                {
                    Path = i.Path ?? "",
                    Name = i.Name ?? "",
                    IsFolder = i.IsFolder ?? false,
                    HasChildren = i.HasChildren ?? false,
                })
                .ToList(),
        };

    /// <summary>Maps a generated script response to the command-facing record.</summary>
    private static StaticFileResponse Map(Gen.ScriptResponseModel? m) =>
        new()
        {
            Path = m?.Path ?? "",
            Name = m?.Name ?? "",
            ParentPath = m?.Parent?.Path,
            Content = m?.Content,
        };

    /// <summary>Maps a generated stylesheet response to the command-facing record.</summary>
    private static StaticFileResponse Map(Gen.StylesheetResponseModel? m) =>
        new()
        {
            Path = m?.Path ?? "",
            Name = m?.Name ?? "",
            ParentPath = m?.Parent?.Path,
            Content = m?.Content,
        };

    /// <summary>Maps a generated partial-view response to the command-facing record.</summary>
    private static StaticFileResponse Map(Gen.PartialViewResponseModel? m) =>
        new()
        {
            Path = m?.Path ?? "",
            Name = m?.Name ?? "",
            ParentPath = m?.Parent?.Path,
            Content = m?.Content,
        };
}
