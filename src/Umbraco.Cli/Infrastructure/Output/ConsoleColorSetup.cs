using Spectre.Console;

namespace Umbraco.Cli.Infrastructure.Output;

/// <summary>
/// Honours the <c>NO_COLOR</c> convention (#94): when the environment variable is present (any
/// value, per https://no-color.org), Spectre.Console output is stripped of colour. Applied once at
/// startup; because the human writer and the auth prompts all use the static <see cref="AnsiConsole"/>,
/// a single global toggle covers them all.
/// </summary>
public static class ConsoleColorSetup
{
    /// <summary>Whether <c>NO_COLOR</c> is set (presence-based, per the convention).</summary>
    /// <param name="value">The raw environment-variable value (null if unset).</param>
    /// <returns>True when colour should be disabled.</returns>
    public static bool NoColorRequested(string? value) => value is not null;

    /// <summary>
    /// Reads <c>NO_COLOR</c> from the environment and disables colour on the global
    /// <see cref="AnsiConsole"/> when it is present. Call once at startup.
    /// </summary>
    public static void ApplyFromEnvironment()
    {
        if (!NoColorRequested(Environment.GetEnvironmentVariable("NO_COLOR")))
            return;
        AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        AnsiConsole.Profile.Capabilities.Ansi = false;
    }
}
