namespace Umbraco.Cli.Commands;

/// <summary>
/// Thrown by <see cref="CommandContextFactory"/> to abort a command before it runs
/// (e.g. no host configured, not authenticated). Distinct from a genuine
/// <see cref="OperationCanceledException"/> so a real Ctrl-C is not mistaken for a
/// configuration error. The error message has already been written to output.
/// </summary>
public sealed class CommandAbortedException : Exception;
