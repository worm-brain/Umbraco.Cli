using System.Diagnostics;

namespace Umbraco.Cli.Commands.Extensions;

/// <summary>
/// The CLI's one process-execution sink (ADR 0010, docs/security-audit.md). It starts an extension
/// command's executable and nothing else, under the ADR's constraints:
/// <list type="bullet">
/// <item>no shell: the executable is started directly (<c>UseShellExecute = false</c>), by the
/// full path <see cref="ExtensionLocator"/> resolved, which is an <c>.exe</c> on Windows;</item>
/// <item>the arguments go as a list (<see cref="ProcessStartInfo.ArgumentList"/>), never joined
/// into one string, so no argument is ever re-split or interpreted;</item>
/// <item>stdin, stdout and stderr are inherited, so the extension talks to the user directly;</item>
/// <item>its exit code is returned as it is, to become the CLI's.</item>
/// </list>
/// </summary>
public static class ExtensionProcess
{
    /// <summary>Runs <paramref name="executable"/> and waits for it to exit.</summary>
    /// <param name="executable">The full path of the executable.</param>
    /// <param name="arguments">Its arguments, passed one by one.</param>
    /// <param name="environment">Variables to add to (or replace in) the inherited environment.</param>
    /// <param name="ct">Cancellation token; cancelling stops waiting, not the process.</param>
    /// <returns>The process's exit code.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The executable could not be started.</exception>
    public static async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct = default
    )
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException($"{executable} did not start.");

        // Ctrl-C reaches the extension as well (it shares the console). Keep this process alive
        // while the extension handles it, so its exit code (130 when it stops) is the one returned,
        // rather than the shell getting its prompt back while the extension still runs.
        ConsoleCancelEventHandler keepWaiting = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += keepWaiting;
        try
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= keepWaiting;
        }
    }
}
