namespace Umbraco.Community.Cli.Client;

/// <summary>Identity of the authenticated back-office user.</summary>
public interface IAuthClient
{
    /// <summary>The back-office user the credentials authenticate as.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current user, or a mapped failure.</returns>
    Task<UmbracoResponse<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken ct = default);
}
