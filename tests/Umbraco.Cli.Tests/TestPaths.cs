namespace Umbraco.Cli.Tests;

/// <summary>Locations in the repository that tests read committed files from.</summary>
internal static class TestPaths
{
    /// <summary>Walks up from the test output folder to the directory holding AGENTS.md.</summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="InvalidOperationException">No ancestor directory contains AGENTS.md.</exception>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find the repo root (AGENTS.md).");
    }
}
