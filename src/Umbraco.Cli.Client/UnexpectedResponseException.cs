namespace Umbraco.Cli.Client;

/// <summary>
/// The server answered, but not in the shape this client was built for (#154): a body that is
/// not JSON, or a read missing a field it always carries (a document with no variants, so no
/// name or dates). Thrown by a mapper instead of substituting a default that would pass drift off
/// as data; the request guard reports it as <see cref="FailureCategory.UnexpectedResponse"/>.
/// </summary>
internal sealed class UnexpectedResponseException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was missing or unreadable, and from which endpoint.</param>
    public UnexpectedResponseException(string message)
        : base(message) { }
}
