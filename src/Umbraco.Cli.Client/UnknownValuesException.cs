namespace Umbraco.Cli.Client;

/// <summary>
/// Input refused because it names values the instance does not know (#278): an unknown webhook
/// event alias, a translation ISO code with no language. It is an
/// <see cref="InvalidArgumentException"/>, so it reports as invalid_argument with no HTTP status,
/// and it also carries the unknown and known values so the command layer can suggest the nearest
/// one. Suggestions are presentation, so they are the CLI's to build, not the API client's.
/// </summary>
public sealed class UnknownValuesException : InvalidArgumentException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was refused and why; reads correctly without a suggestion.</param>
    /// <param name="values">The unknown values and the known set.</param>
    public UnknownValuesException(string message, UnknownValues values)
        : base(message)
    {
        Values = values;
    }

    /// <summary>The unknown values and the known set.</summary>
    public UnknownValues Values { get; }
}
