namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// On/off environment variables (<c>UMBRACO_READONLY</c>, <c>UMBRACO_NO_TOKEN_CACHE</c>), read
/// the same way everywhere so one spelling of "on" works for all of them.
/// </summary>
public static class EnvironmentFlags
{
    /// <summary>Whether the environment variable <paramref name="name"/> is set to a truthy value.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>True for <c>1</c>, <c>true</c> or <c>yes</c> (any case, surrounding spaces ignored).</returns>
    public static bool IsOn(string name) => IsTruthy(Environment.GetEnvironmentVariable(name));

    /// <summary>Whether an environment-variable value should be read as "on" (1/true/yes).</summary>
    /// <param name="value">The raw environment value.</param>
    /// <returns>True for a truthy value.</returns>
    public static bool IsTruthy(string? value)
    {
        // Trim so a stray trailing space (easy to introduce in a Windows `set VAR=1 `) does not
        // silently disable the guardrail.
        var v = value?.Trim();
        return !string.IsNullOrEmpty(v)
            && (
                v == "1"
                || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            );
    }
}
