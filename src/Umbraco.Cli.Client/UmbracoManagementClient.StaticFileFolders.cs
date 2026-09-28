using Microsoft.Kiota.Abstractions;
using Gen = Umbraco.Cli.Client.Generated.Models;

namespace Umbraco.Cli.Client;

/// <summary>
/// Folders for the static-file resources (#238): <c>POST /{kind}/folder</c>,
/// <c>GET</c> and <c>DELETE /{kind}/folder/{path}</c>. Without them a file could not be created
/// under a folder a fresh site does not have (<c>blocklist/Components</c>), and <c>schema apply</c>
/// could not recreate a snapshot's folders (#292). Kept apart from the file operations because the
/// three kinds' folder models differ by generated type name only, so a small switch is clearer than
/// widening the file dispatch map.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>
    /// Creates a folder in a kind's tree, then reads it back so the result is the folder as the
    /// instance holds it, path in Umbraco's <c>/a/b</c> form. The read-back is best-effort, as for
    /// the file creates: when it fails the folder still exists, so the worked-out path is returned.
    /// </summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="name">The folder name (one path segment).</param>
    /// <param name="parentPath">The parent folder path, with or without slashes; null for the root.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created folder, or a mapped failure.</returns>
    public Task<UmbracoResponse<StaticFileFolderResponse>> CreateStaticFileFolderAsync(
        StaticFileKind kind,
        string name,
        string? parentPath,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var parent = NormaliseFolder(parentPath);
                var folder = parent is null
                    ? null
                    : new Gen.FileSystemFolderModel { Path = parent };
                var api = _api.Umbraco.Management.Api.V1;
                switch (kind)
                {
                    case StaticFileKind.Script:
                        await api.Script.Folder.PostAsync(
                            new Gen.CreateScriptFolderRequestModel { Name = name, Parent = folder },
                            cancellationToken: ct
                        );
                        break;
                    case StaticFileKind.Stylesheet:
                        await api.Stylesheet.Folder.PostAsync(
                            new Gen.CreateStylesheetFolderRequestModel
                            {
                                Name = name,
                                Parent = folder,
                            },
                            cancellationToken: ct
                        );
                        break;
                    default:
                        await api.PartialView.Folder.PostAsync(
                            new Gen.CreatePartialViewFolderRequestModel
                            {
                                Name = name,
                                Parent = folder,
                            },
                            cancellationToken: ct
                        );
                        break;
                }

                var path = parent is null ? $"/{name}" : $"/{parent}/{name}";
                try
                {
                    if (await ReadFolderAsync(kind, path, ct) is { Path.Length: > 0 } saved)
                        return saved;
                }
                catch (ApiException)
                {
                    // Fall through: the folder exists, only the read-back failed.
                }
                return new StaticFileFolderResponse
                {
                    Path = path,
                    Name = name,
                    ParentPath = parent is null ? null : $"/{parent}",
                };
            }
        );

    /// <summary>
    /// Deletes a folder by path. Umbraco refuses to delete a folder that is not empty, and that
    /// refusal is returned as the failure.
    /// </summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="path">The folder path (raw, unencoded).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An empty success response, or a mapped failure.</returns>
    public Task<UmbracoResponse<Empty>> DeleteStaticFileFolderAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct = default
    ) =>
        GuardedApiAsync(
            ct,
            async () =>
            {
                var api = _api.Umbraco.Management.Api.V1;
                switch (kind)
                {
                    case StaticFileKind.Script:
                        await api.Script.Folder[path].DeleteAsync(cancellationToken: ct);
                        break;
                    case StaticFileKind.Stylesheet:
                        await api.Stylesheet.Folder[path].DeleteAsync(cancellationToken: ct);
                        break;
                    default:
                        await api.PartialView.Folder[path].DeleteAsync(cancellationToken: ct);
                        break;
                }
                return Empty.Value;
            }
        );

    /// <summary>Reads one folder by path and maps it; the three response models share a shape.</summary>
    /// <param name="kind">Which static-file resource.</param>
    /// <param name="path">The folder path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The folder, with a blank path when the response had no body.</returns>
    private async Task<StaticFileFolderResponse> ReadFolderAsync(
        StaticFileKind kind,
        string path,
        CancellationToken ct
    )
    {
        var api = _api.Umbraco.Management.Api.V1;
        var (folderPath, name, parent) = kind switch
        {
            StaticFileKind.Script => await api.Script.Folder[path].GetAsync(cancellationToken: ct)
                is { } s
                ? (s.Path, s.Name, s.Parent?.Path)
                : (null, null, null),
            StaticFileKind.Stylesheet => await api
                .Stylesheet.Folder[path]
                .GetAsync(cancellationToken: ct)
                is { } c
                ? (c.Path, c.Name, c.Parent?.Path)
                : (null, null, null),
            _ => await api.PartialView.Folder[path].GetAsync(cancellationToken: ct) is { } p
                ? (p.Path, p.Name, p.Parent?.Path)
                : (null, null, null),
        };
        return new StaticFileFolderResponse
        {
            Path = folderPath ?? "",
            Name = name ?? "",
            ParentPath = parent,
        };
    }
}
