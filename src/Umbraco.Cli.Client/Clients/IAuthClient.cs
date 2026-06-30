namespace Umbraco.Cli.Client;

/// <summary>Identity of the authenticated back-office user.</summary>
public interface IAuthClient
{
    Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken ct = default);
}
