using System.Text.RegularExpressions;

namespace Umbraco.Cli.Commands.Extensions;

/// <summary>An extension command found on PATH: its noun and the executable that runs it.</summary>
/// <param name="Noun">The noun it adds, e.g. <c>foo</c> for <c>umbraco foo</c>.</param>
/// <param name="Executable">The full path of the <c>umbraco-foo</c> executable.</param>
public sealed record ExtensionCommand(string Noun, string Executable);

/// <summary>
/// Finds extension commands on PATH (ADR 0010). An extension is an executable named exactly
/// <c>umbraco-&lt;noun&gt;</c>: on Windows an <c>.exe</c> and nothing else (a <c>.cmd</c> or
/// <c>.bat</c> needs <c>cmd.exe</c> to run it, and the CLI never starts a shell), elsewhere a file
/// with an execute bit and no extension. The noun is lower-case kebab-case, like every built-in
/// noun, so a noun can never carry a path separator, a <c>..</c> or a shell metacharacter into the
/// file name. Only absolute PATH entries are searched: a relative one (<c>.</c>, or an empty entry)
/// would run whatever the current directory holds.
/// <para>
/// The PATH value, the platform rule and the executable check are all given to the constructor,
/// so tests can resolve against a made-up PATH without real executables.
/// </para>
/// </summary>
public sealed partial class ExtensionLocator
{
    /// <summary>The file name prefix every extension command's executable has.</summary>
    public const string Prefix = "umbraco-";

    private readonly string[] _directories;
    private readonly bool _windows;
    private readonly Func<string, bool> _isExecutable;

    /// <summary>Creates a locator over a PATH value.</summary>
    /// <param name="path">
    /// The PATH value, entries separated by the running platform's separator; null or empty finds
    /// nothing.
    /// </param>
    /// <param name="windows">True for the Windows rule (<c>.exe</c> only), false for the Unix one.</param>
    /// <param name="isExecutable">
    /// Whether a full file path is an executable to run; by default, on Windows that the file
    /// exists (its name already ends <c>.exe</c>), elsewhere that it exists with an execute bit.
    /// </param>
    public ExtensionLocator(string? path, bool windows, Func<string, bool>? isExecutable = null)
    {
        _directories =
        [
            .. (path ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.TrimEntries)
                // Windows allows an entry to be quoted, e.g. "C:\Program Files\tool".
                .Select(entry => entry.Trim('"'))
                .Where(entry => entry.Length > 0 && Path.IsPathFullyQualified(entry)),
        ];
        _windows = windows;
        _isExecutable = isExecutable ?? (windows ? File.Exists : IsUnixExecutable);
    }

    /// <summary>The locator for this process: its PATH and its platform's rule.</summary>
    /// <returns>The locator.</returns>
    public static ExtensionLocator FromEnvironment() =>
        new(Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows());

    /// <summary>Whether <paramref name="candidate"/> has the shape of a noun: lower-case kebab-case.</summary>
    /// <param name="candidate">The word after <c>umbraco</c>.</param>
    /// <returns>True when it could name an extension.</returns>
    public static bool IsNoun(string candidate) => NounPattern().IsMatch(candidate);

    /// <summary>The executable for <paramref name="noun"/>: the first match in PATH order.</summary>
    /// <param name="noun">The noun.</param>
    /// <returns>Its full path, or null when no PATH directory has one (or the noun is not one).</returns>
    public string? Find(string noun)
    {
        if (!IsNoun(noun))
            return null;
        var fileName = Prefix + noun + (_windows ? ".exe" : "");
        return _directories
            .Select(dir => Path.Combine(dir, fileName))
            .FirstOrDefault(_isExecutable);
    }

    /// <summary>
    /// Every extension on PATH, without running any of them: for each noun, the executable
    /// <see cref="Find"/> would run. A directory that cannot be read is skipped.
    /// </summary>
    /// <returns>The extensions, ordered by noun.</returns>
    public IReadOnlyList<ExtensionCommand> List()
    {
        var found = new Dictionary<string, ExtensionCommand>(StringComparer.Ordinal);
        foreach (var dir in _directories)
        {
            foreach (var file in FilesIn(dir))
            {
                var name = Path.GetFileName(file);
                if (_windows && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                var noun = (_windows ? name[..^".exe".Length] : name)[Prefix.Length..];
                // TryAdd keeps the first directory's executable, as Find would.
                if (IsNoun(noun) && _isExecutable(file))
                    found.TryAdd(noun, new ExtensionCommand(noun, file));
            }
        }
        return [.. found.Values.OrderBy(e => e.Noun, StringComparer.Ordinal)];
    }

    /// <summary>The files in <paramref name="dir"/> named with the prefix, or none when it cannot be read.</summary>
    /// <param name="dir">A PATH directory.</param>
    /// <returns>The full paths.</returns>
    private static IEnumerable<string> FilesIn(string dir)
    {
        try
        {
            return [.. Directory.EnumerateFiles(dir, Prefix + "*")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A PATH entry that is missing or unreadable is common and harmless.
            return [];
        }
    }

    /// <summary>Whether a Unix file exists and anyone may execute it; always false on Windows, which has no execute bit.</summary>
    /// <param name="file">The full path.</param>
    /// <returns>True for an executable file.</returns>
    private static bool IsUnixExecutable(string file) =>
        !OperatingSystem.IsWindows()
        && File.Exists(file)
        && (
            File.GetUnixFileMode(file)
            & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)
        ) != 0;

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex NounPattern();
}
