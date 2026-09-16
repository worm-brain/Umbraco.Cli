using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Computes the strings shown by <c>umbraco --version</c> (#95): the tool version, the target
/// framework moniker it was built for, and the .NET runtime it is running on. The parsing helpers
/// are pure and public so they can be unit-tested without constructing an assembly.
/// </summary>
public static class VersionInfo
{
    /// <summary>
    /// Resolves the tool's display version from <paramref name="assembly"/>: its informational
    /// version (with any SourceLink build-metadata suffix removed), falling back to the assembly
    /// version.
    /// </summary>
    /// <param name="assembly">The assembly to read version attributes from.</param>
    /// <returns>The display version, or <c>"unknown"</c> when none is available.</returns>
    public static string Version(Assembly assembly) =>
        NormalizeVersion(
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion,
            assembly.GetName().Version
        );

    /// <summary>
    /// Normalizes a raw informational version into a display version: everything before the first
    /// <c>+</c> (the build-metadata separator, e.g. <c>0.1.0+3fa6653</c> from SourceLink), falling
    /// back to <paramref name="assemblyVersion"/> and then <c>"unknown"</c>.
    /// </summary>
    /// <param name="informationalVersion">The raw <see cref="AssemblyInformationalVersionAttribute"/> value, or null.</param>
    /// <param name="assemblyVersion">The assembly version to fall back to, or null.</param>
    /// <returns>The normalized display version, never null or empty.</returns>
    public static string NormalizeVersion(
        string? informationalVersion,
        System.Version? assemblyVersion
    )
    {
        var version = informationalVersion?.Split('+', 2)[0];
        if (string.IsNullOrWhiteSpace(version))
            version = assemblyVersion?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
    }

    /// <summary>
    /// Resolves the target-framework moniker (e.g. <c>net9.0</c>) <paramref name="assembly"/> was
    /// built for, from its <see cref="TargetFrameworkAttribute"/>.
    /// </summary>
    /// <param name="assembly">The assembly to read the target framework from.</param>
    /// <returns>The framework moniker, or <c>"unknown"</c> when it cannot be determined.</returns>
    public static string TargetFramework(Assembly assembly) =>
        ToMoniker(assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);

    /// <summary>
    /// Converts a <see cref="TargetFrameworkAttribute.FrameworkName"/> (e.g.
    /// <c>.NETCoreApp,Version=v9.0</c>) into its short moniker (e.g. <c>net9.0</c>). Unrecognized
    /// framework identifiers are returned unchanged so no information is lost.
    /// </summary>
    /// <param name="frameworkName">The full framework name, or null.</param>
    /// <returns>The short moniker, the original name when unrecognized, or <c>"unknown"</c> for null.</returns>
    public static string ToMoniker(string? frameworkName)
    {
        if (string.IsNullOrWhiteSpace(frameworkName))
            return "unknown";

        var parts = frameworkName.Split(',', 2);
        var identifier = parts[0];
        var version =
            parts.Length > 1
                ? parts[1].Replace("Version=v", "", StringComparison.OrdinalIgnoreCase)
                : "";

        return identifier switch
        {
            ".NETCoreApp" => $"net{version}",
            _ => frameworkName,
        };
    }

    /// <summary>Gets a human description of the running .NET runtime (e.g. <c>.NET 9.0.9</c>).</summary>
    /// <returns>The runtime description.</returns>
    public static string Runtime() => RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// Builds the multi-line text printed by <c>umbraco --version</c>: the tool version, target
    /// framework and runtime.
    /// </summary>
    /// <param name="assembly">The tool's entry assembly.</param>
    /// <returns>The formatted version block.</returns>
    public static string Format(Assembly assembly) =>
        $"umbraco {Version(assembly)}"
        + $"{Environment.NewLine}target framework: {TargetFramework(assembly)}"
        + $"{Environment.NewLine}runtime: {Runtime()}";
}
