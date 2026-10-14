namespace Umbraco.Community.Cli.Client;

/// <summary>
/// Produces an <see cref="IUmbracoManagementClient"/> for a configured
/// <see cref="HttpClient"/> (base address + bearer token already set). The seam that
/// lets a command run against the real HTTP client in production and an in-memory
/// adapter in tests.
/// </summary>
public interface IUmbracoManagementClientFactory
{
    /// <summary>Creates the client a command calls the Management API through.</summary>
    /// <param name="http">The HTTP client, with its base address and bearer token already set.</param>
    /// <returns>The Management API client.</returns>
    IUmbracoManagementClient Create(HttpClient http);
}
