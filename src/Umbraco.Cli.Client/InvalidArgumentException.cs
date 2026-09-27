using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// The caller's input was refused before anything was sent: an unknown webhook event alias, a
/// translation ISO code the instance does not have, a <c>--json-body</c> that is not a JSON object
/// (#280). Like <see cref="UnresolvedReferenceException"/>, the request guard reports it as
/// <see cref="FailureCategory.InvalidArgument"/> with no HTTP status, because no server rejected
/// anything. It stays an <see cref="ApiException"/> carrying 400, so code that tests for a bad
/// request still sees one. The command layer's twin, for input the CLI refuses before calling the
/// client, is <c>Umbraco.Cli.Commands.InvalidInputException</c>.
/// </summary>
public class InvalidArgumentException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong with the input, and how to fix it.</param>
    /// <param name="status">
    /// The HTTP status it stands for, kept for code that tests for one (400 by default; 404/409
    /// for <see cref="UnresolvedReferenceException"/>). The request guard reports no status.
    /// </param>
    public InvalidArgumentException(string message, int status = 400)
        : base(message)
    {
        ResponseStatusCode = status;
    }
}
