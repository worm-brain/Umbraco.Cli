namespace Umbraco.Community.Cli.Client;

/// <summary>Production adapter: wraps the configured <see cref="HttpClient"/> in the real HTTP client.</summary>
public sealed class UmbracoManagementClientFactory : IUmbracoManagementClientFactory
{
    /// <inheritdoc />
    public IUmbracoManagementClient Create(HttpClient http) => new UmbracoManagementClient(http);
}
