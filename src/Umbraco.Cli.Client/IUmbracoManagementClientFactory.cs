namespace Umbraco.Cli.Client;

/// <summary>
/// Produces an <see cref="IUmbracoManagementClient"/> for a configured
/// <see cref="HttpClient"/> (base address + bearer token already set). The seam that
/// lets a command run against the real HTTP client in production and an in-memory
/// adapter in tests.
/// </summary>
public interface IUmbracoManagementClientFactory
{
    IUmbracoManagementClient Create(HttpClient http);
}
