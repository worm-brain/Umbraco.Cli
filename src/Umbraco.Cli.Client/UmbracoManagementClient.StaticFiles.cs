using Microsoft.Kiota.Abstractions;
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
    /// The six per-kind operations. Each closes over the right generated request builder and maps
    /// to the shared command-facing records; the delegates do the raw call and the public methods
    /// add the <see cref="GuardedApiAsync{T}"/> envelope/guard once.
    /// </summary>
    private sealed record StaticFileOps(
        Func<string?, int, int, CancellationToken, Task<PagedResponse<StaticFileTreeItem>>> List,
        Func<string, CancellationToken, Task<StaticFileResponse>> Get,
        Func<CreateStaticFileRequest, CancellationToken, Task<Empty>> Create,
        Func<string, UpdateStaticFileRequest, CancellationToken, Task<Empty>> Update,
        Func<string, CancellationToken, Task<Empty>> Delete,
        Func<string, string, CancellationToken, Task<Empty>> Rename
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

    /// <summary>
    /// Creates a script, stylesheet or partial view, then reads it back so the result is the file
    /// as <c>get</c> shows it (#296): the path in Umbraco's own <c>/folder/name</c> form, not one
    /// built from the flags as typed. The create response is empty, so the path to read is worked
    /// out from the normalised parent and the name. The read-back is best-effort, as for the other
    /// creates: when it fails the create still succeeded, so the same normalised path is returned
    /// with the request's fields.
    /// </summary>
    /// <param name="kind">Which kind of file.</param>
    /// <param name="request">The file to create; its parent may have leading or trailing slashes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created file, or a mapped failure.</returns>
    public Task<UmbracoResponse<StaticFileResponse>> CreateStaticFileAsync(
        StaticFileKind kind,
        CreateStaticFileRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var ops = StaticFileOpsMap[kind];
                var normalised = request with { ParentPath = NormaliseFolder(request.ParentPath) };
                await ops.Create(normalised, ct);
                try
                {
                    // An empty read (no body) maps to a blank path: treat it as a failed read.
                    if (await ops.Get(CreatedPath(normalised), ct) is { Path.Length: > 0 } saved)
                        return saved;
                }
                catch (ApiException)
                {
                    // Fall through: the file exists, only the read-back failed.
                }
                return Echo(normalised);
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateStaticFileAsync(
        StaticFileKind kind,
        string path,
        UpdateStaticFileRequest request,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].Update(path, request, ct));

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> RenameStaticFileAsync(
        StaticFileKind kind,
        string path,
        string name,
        CancellationToken ct = default
    ) => GuardedApiAsync(ct, () => StaticFileOpsMap[kind].Rename(path, name, ct));

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
                                c =>
                                {
                                    c.QueryParameters.Skip = skip;
                                    c.QueryParameters.Take = take;
                                },
                                ct
                            )
                            : await api.Tree.Script.Children.GetAsync(
                                c =>
                                {
                                    c.QueryParameters.ParentPath = parent;
                                    c.QueryParameters.Skip = skip;
                                    c.QueryParameters.Take = take;
                                },
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
                    return Empty.Value;
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
                },
                Rename: async (path, name, ct) =>
                {
                    await api.Script[path]
                        .Rename.PutAsync(
                            new Gen.RenameScriptRequestModel { Name = name },
                            cancellationToken: ct
                        );
                    return Empty.Value;
                }
            ),
            [StaticFileKind.Stylesheet] = new StaticFileOps(
                List: async (parent, skip, take, ct) =>
                    MapTree(
                        parent is null
                            ? await api.Tree.Stylesheet.Root.GetAsync(
                                c =>
                                {
                                    c.QueryParameters.Skip = skip;
                                    c.QueryParameters.Take = take;
                                },
                                ct
                            )
                            : await api.Tree.Stylesheet.Children.GetAsync(
                                c =>
                                {
                                    c.QueryParameters.ParentPath = parent;
                                    c.QueryParameters.Skip = skip;
                                    c.QueryParameters.Take = take;
                                },
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
                    return Empty.Value;
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
                },
                Rename: async (path, name, ct) =>
                {
                    await api.Stylesheet[path]
                        .Rename.PutAsync(
                            new Gen.RenameStylesheetRequestModel { Name = name },
                            cancellationToken: ct
                        );
                    return Empty.Value;
                }
            ),
            [StaticFileKind.PartialView] = new StaticFileOps(
                List: async (parent, skip, take, ct) =>
                    MapTree(
                        parent is null
                            ? await api.Tree.PartialView.Root.GetAsync(
                                c =>
                                {
                                    c.QueryParameters.Skip = skip;
                                    c.QueryParameters.Take = take;
                                },
                                ct
                            )
                            : await api.Tree.PartialView.Children.GetAsync(
                                c =>
                                {
                                    c.QueryParameters.ParentPath = parent;
                                    c.QueryParameters.Skip = skip;
                                    c.QueryParameters.Take = take;
                                },
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
                    return Empty.Value;
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
                },
                Rename: async (path, name, ct) =>
                {
                    await api.PartialView[path]
                        .Rename.PutAsync(
                            new Gen.RenamePartialViewRequestModel { Name = name },
                            cancellationToken: ct
                        );
                    return Empty.Value;
                }
            ),
        };
    }

    /// <summary>The parent-folder reference for a create, or null at the tree root.</summary>
    private static Gen.FileSystemFolderModel? Folder(string? parentPath) =>
        parentPath is null ? null : new Gen.FileSystemFolderModel { Path = parentPath };

    /// <summary>
    /// A <c>--parent</c> folder path without leading or trailing slashes (<c>/blocklist/</c> becomes
    /// <c>blocklist</c>), so the path typed and the path <c>list</c> prints both work (#296). Blank
    /// or <c>/</c> alone means the root.
    /// </summary>
    /// <param name="parentPath">The folder path as typed, or null.</param>
    /// <returns>The trimmed path, or null for the root.</returns>
    internal static string? NormaliseFolder(string? parentPath) =>
        parentPath?.Trim().Trim('/') is { Length: > 0 } trimmed ? trimmed : null;

    /// <summary>
    /// The path a file has after a rename (#365): the same folder, the new name. The folder part
    /// keeps the form it was given in (<c>/theme/a.css</c> renamed to <c>b.css</c> is
    /// <c>/theme/b.css</c>, <c>a.css</c> is <c>b.css</c>), which <c>get</c> accepts either way.
    /// </summary>
    /// <param name="path">The file's path before the rename.</param>
    /// <param name="name">The new file name.</param>
    /// <returns>The file's path after the rename.</returns>
    public static string RenamedPath(string path, string name)
    {
        var trimmed = path.Trim().TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? name : trimmed[..(slash + 1)] + name;
    }

    /// <summary>
    /// The path a just-created file has, in Umbraco's form: a leading <c>/</c>, then the folder
    /// and the name joined with <c>/</c> (<c>/blocklist/Components/title.cshtml</c>).
    /// </summary>
    /// <param name="req">The create request, with its parent already normalised.</param>
    /// <returns>The file's path.</returns>
    internal static string CreatedPath(CreateStaticFileRequest req) =>
        req.ParentPath is null ? $"/{req.Name}" : $"/{req.ParentPath}/{req.Name}";

    /// <summary>
    /// The fallback result when the read-back after a create fails (#296): the request's fields
    /// under the path the file was created at, in the same form <c>get</c> prints.
    /// </summary>
    /// <param name="req">The create request, with its parent already normalised.</param>
    /// <returns>The file as far as the request describes it.</returns>
    private static StaticFileResponse Echo(CreateStaticFileRequest req) =>
        new()
        {
            Path = CreatedPath(req),
            Name = req.Name,
            ParentPath = req.ParentPath is null ? null : $"/{req.ParentPath}",
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
