namespace Umbraco.Cli.Commands;

/// <summary>
/// Thrown when the CLI refuses to run a write because it would take more with it than the caller
/// asked for - e.g. deleting a type that content still uses (#246, #252, #253). The executor
/// reports the message and exits <c>2</c> (aborted), like a missing <c>--yes</c>: nothing was
/// changed.
/// </summary>
/// <param name="message">Why it was refused and how to proceed anyway.</param>
public sealed class SafetyRefusalException(string message) : Exception(message);
