namespace Umbraco.Cli.Client;

/// <summary>
/// The site-capabilities read on <see cref="UmbracoManagementClient"/> (ADR 0009, #440): the
/// package manifests, resolved once per client against the CLI's vocabulary.
/// </summary>
public sealed partial class UmbracoManagementClient
{
    /// <summary>The resolved capabilities, once read; null until then.</summary>
    private SiteCapabilities? _siteCapabilities;

    /// <inheritdoc />
    public async Task<SiteCapabilities> GetSiteCapabilitiesAsync(CancellationToken ct = default)
    {
        // Cached per client, like GetServerVersionAsync: a command that needs a capability pays
        // for one manifest read, and a failed read is not retried.
        if (_siteCapabilities is not null)
            return _siteCapabilities;

        var manifests = await GetManifestsAsync(ManifestScope.All, ct);
        _siteCapabilities = manifests.IsSuccess
            ? SiteCapabilities.From(manifests.Data ?? [])
            : SiteCapabilities.None(
                manifests.ErrorMessage ?? $"the manifest read failed ({manifests.StatusCode})"
            );
        return _siteCapabilities;
    }
}
