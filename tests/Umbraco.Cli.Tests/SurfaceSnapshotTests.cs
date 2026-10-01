using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Config;

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
        var path = Path.Combine(TestPaths.RepoRoot(), "docs", "surface.json");
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
    public void MachineNeutral_DefaultConfigPath_IsReplacedWithThePlaceholder()
    {
        // Arrange
        var json = JsonSerializer.Serialize(
            new { description = $"Path (default: {ConfigStore.DefaultConfigPath})." },
            Options
        );

        // Act
        var neutral = MachineNeutral(json);

        // Assert
        Assert.Contains($"(default: {DefaultConfigPathPlaceholder}).", neutral);
    }

    [Fact]
    public void MachineNeutral_TextWithoutThePath_IsUnchanged()
    {
        var json = """{"description":"no path here"}""";

        Assert.Equal(json, MachineNeutral(json));
    }

    [Fact]
    public void FirstDifference_ReportsTheFirstMismatchedLine()
    {
        var message = FirstDifference("a\nb\nc", "a\nx\nc");

        Assert.Equal("first difference at line 2: expected 'b', got 'x'", message);
    }

    /// <summary>
    /// Stands in for the machine-specific default config path (it embeds the OS user profile)
    /// so the snapshot renders identically on every OS and machine.
    /// </summary>
    private const string DefaultConfigPathPlaceholder = "<user config dir>/Umbraco/config.json";

    /// <summary>
    /// Serialises the shipped tree's catalog with LF line endings and a trailing newline, with
    /// the resolved default config path replaced by <see cref="DefaultConfigPathPlaceholder"/>.
    /// </summary>
    /// <returns>The machine-neutral snapshot text.</returns>
    private static string Render() =>
        MachineNeutral(
            Normalise(
                JsonSerializer.Serialize(CommandCatalog.Describe(TestCliRoot.Build()), Options)
            )
        ) + "\n";

    /// <summary>
    /// Replaces this machine's default config path, in its JSON-escaped form (backslashes are
    /// doubled on Windows), with <see cref="DefaultConfigPathPlaceholder"/>.
    /// </summary>
    /// <param name="json">Serialised catalog JSON.</param>
    /// <returns>The JSON with the machine-specific path removed.</returns>
    private static string MachineNeutral(string json) =>
        json.Replace(
            JsonSerializer.Serialize(ConfigStore.DefaultConfigPath, Options).Trim('"'),
            DefaultConfigPathPlaceholder
        );

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
}
