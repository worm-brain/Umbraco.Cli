using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Thrown by <see cref="CommandContextFactory"/> to abort a command before it runs
/// (e.g. no host configured, not authenticated). Distinct from a genuine
/// <see cref="OperationCanceledException"/> so a real Ctrl-C is not mistaken for a
/// configuration error. The error message has already been written to output.
/// </summary>
/// <param name="exitCode">
/// The exit code the error was written with: <see cref="ExitCode.Aborted"/> for a policy or
/// credentials problem, <see cref="ExitCode.Failed"/> when the token exchange never reached the
/// server (#445), which is reported like any other unreachable request.
/// </param>
public sealed class CommandAbortedException(ExitCode exitCode = ExitCode.Aborted) : Exception
{
    /// <summary>The process exit code to return; it matches the <c>exitCode</c> in the written error.</summary>
    public ExitCode ExitCode { get; } = exitCode;
}
