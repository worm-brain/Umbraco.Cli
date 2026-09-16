using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Static-file resources (scripts, stylesheets, partial views) on
/// <see cref="UmbracoManagementClient"/> (issue #105). The three share an identical path-addressed
/// API shape and differ only by generated type names, so each operation switches on
/// <see cref="StaticFileKind"/> to reach the right generated builder and maps the result onto a
/// single command-facing record. Paths are passed to the generated string indexers raw - Kiota
/// percent-encodes them (slashes included) when building the request.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <inheritdoc />
    public Task<UmbracoResponse<PagedResponse<StaticFileTreeItem>>> GetStaticFilesAsync(
        StaticFileKind kind,
        string? parentPath = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                // All three tree endpoints return the same shared paged model, so only the builder
                // call differs per kind.
                // The generated per-builder query-parameter types are distinct, so the config
                // lambdas are inlined per arm rather than shared (their property names match).
                var paged = kind switch
                {
                    StaticFileKind.Script => parentPath is null
                        ? await _api.Umbraco.Management.Api.V1.Tree.Script.Root.GetAsync(
                            c =>
                            {
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        )
                        : await _api.Umbraco.Management.Api.V1.Tree.Script.Children.GetAsync(
                            c =>
                            {
                                c.QueryParameters.ParentPath = parentPath;
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        ),
                    StaticFileKind.Stylesheet => parentPath is null
                        ? await _api.Umbraco.Management.Api.V1.Tree.Stylesheet.Root.GetAsync(
                            c =>
                            {
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        )
                        : await _api.Umbraco.Management.Api.V1.Tree.Stylesheet.Children.GetAsync(
                            c =>
                            {
                                c.QueryParameters.ParentPath = parentPath;
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        ),
                    StaticFileKind.PartialView => parentPath is null
                        ? await _api.Umbraco.Management.Api.V1.Tree.PartialView.Root.GetAsync(
                            c =>
                            {
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        )
                        : await _api.Umbraco.Management.Api.V1.Tree.PartialView.Children.GetAsync(
                            c =>
                            {
                                c.QueryParameters.ParentPath = parentPath;
                                c.QueryParameters.Skip = skip;
                                c.QueryParameters.Take = take;
                            },
                            ct
                        ),
                    _ => throw UnknownKind(kind),
                };

                return new PagedResponse<StaticFileTreeItem>
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
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<StaticFileResponse>> GetStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
                kind switch
                {
                    // Each response type is distinct but carries the same fields; map per arm.
                    StaticFileKind.Script => Map(
                        await _api
                            .Umbraco.Management.Api.V1.Script[path]
                            .GetAsync(cancellationToken: ct)
                    ),
                    StaticFileKind.Stylesheet => Map(
                        await _api
                            .Umbraco.Management.Api.V1.Stylesheet[path]
                            .GetAsync(cancellationToken: ct)
                    ),
                    StaticFileKind.PartialView => Map(
                        await _api
                            .Umbraco.Management.Api.V1.PartialView[path]
                            .GetAsync(cancellationToken: ct)
                    ),
                    _ => throw UnknownKind(kind),
                }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<StaticFileResponse>> CreateStaticFileAsync(
        StaticFileKind kind,
        CreateStaticFileRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var parent = request.ParentPath is null
                    ? null
                    : new Gen.FileSystemFolderModel { Path = request.ParentPath };

                switch (kind)
                {
                    case StaticFileKind.Script:
                        await _api.Umbraco.Management.Api.V1.Script.PostAsync(
                            new Gen.CreateScriptRequestModel
                            {
                                Name = request.Name,
                                Content = request.Content,
                                Parent = parent,
                            },
                            cancellationToken: ct
                        );
                        break;
                    case StaticFileKind.Stylesheet:
                        await _api.Umbraco.Management.Api.V1.Stylesheet.PostAsync(
                            new Gen.CreateStylesheetRequestModel
                            {
                                Name = request.Name,
                                Content = request.Content,
                                Parent = parent,
                            },
                            cancellationToken: ct
                        );
                        break;
                    case StaticFileKind.PartialView:
                        await _api.Umbraco.Management.Api.V1.PartialView.PostAsync(
                            new Gen.CreatePartialViewRequestModel
                            {
                                Name = request.Name,
                                Content = request.Content,
                                Parent = parent,
                            },
                            cancellationToken: ct
                        );
                        break;
                    default:
                        throw UnknownKind(kind);
                }

                // The create response is empty; echo the request with the derived path (Umbraco
                // paths join with a forward slash under the parent folder).
                var path = request.ParentPath is null
                    ? request.Name
                    : $"{request.ParentPath.TrimEnd('/')}/{request.Name}";
                return new StaticFileResponse
                {
                    Path = path,
                    Name = request.Name,
                    ParentPath = request.ParentPath,
                    Content = request.Content,
                };
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> UpdateStaticFileAsync(
        StaticFileKind kind,
        string path,
        UpdateStaticFileRequest request,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                switch (kind)
                {
                    case StaticFileKind.Script:
                        await _api
                            .Umbraco.Management.Api.V1.Script[path]
                            .PutAsync(
                                new Gen.UpdateScriptRequestModel { Content = request.Content },
                                cancellationToken: ct
                            );
                        break;
                    case StaticFileKind.Stylesheet:
                        await _api
                            .Umbraco.Management.Api.V1.Stylesheet[path]
                            .PutAsync(
                                new Gen.UpdateStylesheetRequestModel { Content = request.Content },
                                cancellationToken: ct
                            );
                        break;
                    case StaticFileKind.PartialView:
                        await _api
                            .Umbraco.Management.Api.V1.PartialView[path]
                            .PutAsync(
                                new Gen.UpdatePartialViewRequestModel { Content = request.Content },
                                cancellationToken: ct
                            );
                        break;
                    default:
                        throw UnknownKind(kind);
                }
                return Empty.Value;
            }
        );

    /// <inheritdoc />
    public Task<UmbracoResponse<Empty>> DeleteStaticFileAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                switch (kind)
                {
                    case StaticFileKind.Script:
                        await _api
                            .Umbraco.Management.Api.V1.Script[path]
                            .DeleteAsync(cancellationToken: ct);
                        break;
                    case StaticFileKind.Stylesheet:
                        await _api
                            .Umbraco.Management.Api.V1.Stylesheet[path]
                            .DeleteAsync(cancellationToken: ct);
                        break;
                    case StaticFileKind.PartialView:
                        await _api
                            .Umbraco.Management.Api.V1.PartialView[path]
                            .DeleteAsync(cancellationToken: ct);
                        break;
                    default:
                        throw UnknownKind(kind);
                }
                return Empty.Value;
            }
        );

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

    /// <summary>Builds an <see cref="InvalidOperationException"/> for an unmapped kind (unreachable).</summary>
    private static InvalidOperationException UnknownKind(StaticFileKind kind) =>
        new($"Unknown static-file kind '{kind}'.");
}
