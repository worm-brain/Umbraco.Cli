using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The action behind <c>umbraco --version</c> (#95). Replaces System.CommandLine's default version
/// action so the output includes the tool version, target framework and runtime rather than just
/// the bare assembly version.
/// </summary>
public sealed class VersionCommandAction : SynchronousCommandLineAction
{
    /// <summary>
    /// Writes the formatted version block (see <see cref="VersionInfo.Format"/>) to the invocation's
    /// output stream.
    /// </summary>
    /// <param name="parseResult">The parse result carrying the invocation configuration.</param>
    /// <returns>Exit code 0.</returns>
    public override int Invoke(ParseResult parseResult)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(VersionCommandAction).Assembly;
        parseResult.InvocationConfiguration.Output.WriteLine(VersionInfo.Format(assembly));
        return 0;
    }
}
