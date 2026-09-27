using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Golden test for the public command surface. The whole shipped tree (every noun, verb,
/// argument, option, type, default and safety flag - the same data <c>umbraco commands</c>
/// emits) is committed as <c>docs/surface.json</c>, so any surface change shows up as one
/// reviewable diff instead of slipping in unnoticed. To accept an intended change, rerun with
/// <c>UPDATE_SURFACE=1</c> and commit the regenerated file.
/// </summary>
public class SurfaceSnapshotTests
{
    /// <summary>The same shape the CLI emits, with readable (unescaped) non-ASCII for diffs.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void Surface_MatchesCommittedSnapshot()
    {
        // Arrange
        var path = Path.Combine(RepoRoot(), "docs", "surface.json");
        var actual = Render();

        // Opt-in regeneration: the only sanctioned way to change the committed file.
        if (Environment.GetEnvironmentVariable("UPDATE_SURFACE") == "1")
            File.WriteAllText(path, actual);

        var expected = File.Exists(path) ? Normalise(File.ReadAllText(path)) : "";

        // Act + Assert: a bare Assert.Equal on a ~10k-line file is unreadable, so name the
        // first differing line and say how to accept the change.
        Assert.True(
            expected == actual,
            $"The CLI surface differs from docs/surface.json ({FirstDifference(expected, actual)}). "
                + "If the change is intended, run `UPDATE_SURFACE=1 dotnet test --filter "
                + "SurfaceSnapshotTests` and commit docs/surface.json."
        );
    }

    [Fact]
    public void FirstDifference_ReportsTheFirstMismatchedLine()
    {
        var message = FirstDifference("a\nb\nc", "a\nx\nc");

        Assert.Equal("first difference at line 2: expected 'b', got 'x'", message);
    }

    /// <summary>Serialises the shipped tree's catalog with LF line endings and a trailing newline.</summary>
    private static string Render() =>
        Normalise(JsonSerializer.Serialize(CommandCatalog.Describe(TestCliRoot.Build()), Options))
        + "\n";

    /// <summary>Normalises CRLF so a Windows checkout compares equal to the committed LF file.</summary>
    private static string Normalise(string text) => text.Replace("\r\n", "\n");

    /// <summary>Describes the first line at which two texts diverge.</summary>
    /// <param name="expected">The committed text.</param>
    /// <param name="actual">The freshly rendered text.</param>
    /// <returns>A short human description of the first mismatch.</returns>
    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end of file>";
            var al = i < a.Length ? a[i] : "<end of file>";
            if (el != al)
                return $"first difference at line {i + 1}: expected '{el.Trim()}', got '{al.Trim()}'";
        }
        return "no difference";
    }

    /// <summary>Walks up from the test output folder to the directory holding AGENTS.md.</summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="InvalidOperationException">No ancestor directory contains AGENTS.md.</exception>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find the repo root (AGENTS.md).");
    }
}
