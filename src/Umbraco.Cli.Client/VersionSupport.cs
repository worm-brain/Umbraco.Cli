namespace Umbraco.Cli.Client;

/// <summary>How a server version compares with the range this build supports (#153).</summary>
public enum VersionFit
{
    /// <summary>The version is missing or not a version this can parse; nothing is claimed.</summary>
    Unknown = 0,

    /// <summary>The version's major is inside the tested range.</summary>
    Supported,

    /// <summary>The version's major is outside the tested range.</summary>
    Unsupported,
}

/// <summary>
/// The Umbraco versions this build of the client was generated and tested against (#153): the
/// single source of truth for the range. The committed <c>spec/management.json</c> says only
/// <c>"version": "Latest"</c>, so the range is declared here instead. <b>Update it</b> whenever the
/// spec is refreshed from a new Umbraco major (<c>scripts/fetch-spec.ps1</c>, then
/// <c>scripts/regen-client.ps1</c>) or a major is added to the tested set. Checked by major only:
/// the Management API keeps its contract within a major.
/// </summary>
public static class VersionSupport
{
    /// <summary>The oldest tested Umbraco major: the spec the client is generated from.</summary>
    public const int MinMajor = 17;

    /// <summary>The newest tested Umbraco major.</summary>
    public const int MaxMajor = 18;

    /// <summary>The range as people read it, e.g. <c>17.x-18.x</c>.</summary>
    public static string Range =>
        MinMajor == MaxMajor ? $"{MinMajor}.x" : $"{MinMajor}.x-{MaxMajor}.x";

    /// <summary>
    /// Compares <paramref name="serverVersion"/> (as <c>server/information</c> reports it, e.g.
    /// <c>17.3.5</c>, <c>18.0.0-rc2</c> or <c>17.3.5+build</c>) with the tested range.
    /// </summary>
    /// <param name="serverVersion">The connected server's version, or null when unknown.</param>
    /// <returns>Whether the version's major is in range, or <see cref="VersionFit.Unknown"/> when it cannot be read.</returns>
    public static VersionFit Check(string? serverVersion)
    {
        // Only the major matters, so read the digits before the first '.', '-' or '+'.
        var majorText = serverVersion?.Trim().Split('.', '-', '+')[0];
        if (!int.TryParse(majorText, out var major))
            return VersionFit.Unknown;
        return major is >= MinMajor and <= MaxMajor ? VersionFit.Supported : VersionFit.Unsupported;
    }

    /// <summary>
    /// The warning for a version outside the range, naming both versions. It names no CLI command,
    /// so any caller can use it.
    /// </summary>
    /// <param name="serverVersion">The connected server's version.</param>
    /// <returns>The warning.</returns>
    public static string OutOfRangeMessage(string serverVersion) =>
        $"The instance runs Umbraco {serverVersion}, outside the range this CLI was built and "
        + $"tested against (Umbraco {Range}). Commands may fail or return unexpected data.";
}
