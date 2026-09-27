using Microsoft.Kiota.Abstractions;

namespace Umbraco.Cli.Client;

/// <summary>
/// An alias, name or key the caller typed matched no item (404) or more than one (409). The
/// caller's input is what needs fixing, so the request guard reports it as
/// <see cref="FailureCategory.InvalidArgument"/> with no HTTP status (#256), rather than as a
/// server 404 that sends a caller looking for a server problem. It is the reference-shaped case
/// of <see cref="InvalidArgumentException"/>, carrying the status, so code that tests for a 404
/// still sees one.
/// </summary>
public sealed class UnresolvedReferenceException : InvalidArgumentException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What did not resolve, and how to find a valid value.</param>
    /// <param name="status">404 for no match, 409 for an ambiguous name.</param>
    public UnresolvedReferenceException(string message, int status)
        : base(message, status) { }
}
