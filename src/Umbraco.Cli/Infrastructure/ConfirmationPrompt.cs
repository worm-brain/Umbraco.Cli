namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Asks the user to confirm a destructive operation (#70). Abstracted so the confirmation
/// behaviour is injectable — the executor never touches the console directly, which keeps the
/// destructive-op gate deterministically testable (the real implementation depends on
/// <see cref="Console.IsInputRedirected"/>, an OS-level signal a unit test cannot control).
/// </summary>
public interface IConfirmationPrompt
{
    /// <summary>
    /// Whether a real user can be prompted. False when input is redirected (piped, scripted,
    /// or run by an agent) — in that case the executor refuses the destructive op unless
    /// <c>--yes</c> was given, rather than blocking on input that will never come.
    /// </summary>
    bool IsInteractive { get; }

    /// <summary>
    /// Prompts for confirmation and returns the answer. Only called when
    /// <see cref="IsInteractive"/> is true.
    /// </summary>
    /// <param name="message">The confirmation question to show.</param>
    /// <returns>True only if the user explicitly confirmed.</returns>
    bool Confirm(string message);
}

/// <summary>
/// Console-backed <see cref="IConfirmationPrompt"/>: the prompt is written to stderr (so
/// stdout stays clean for machine-readable output) and the answer read from stdin. Only
/// <c>y</c>/<c>yes</c> (case-insensitive) confirms; anything else — including a blank line or
/// end-of-input — is treated as "no", so the default is to NOT proceed.
/// </summary>
public sealed class ConsoleConfirmationPrompt : IConfirmationPrompt
{
    /// <inheritdoc />
    public bool IsInteractive => !Console.IsInputRedirected;

    /// <inheritdoc />
    public bool Confirm(string message)
    {
        Console.Error.Write($"{message} [y/N] ");
        var answer = Console.ReadLine()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
