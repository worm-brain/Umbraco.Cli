using Umbraco.Cli.Commands.Extensions;

namespace Umbraco.Cli.Tests;

/// <summary>
/// How an extension command's executable is found on PATH (ADR 0010). Each test builds its own
/// PATH out of temporary directories holding empty files, so no real executable is needed, and
/// passes the platform rule explicitly, so both rules are checked on every OS.
/// </summary>
public sealed class ExtensionLocatorTests : IDisposable
{
    private readonly List<string> _dirs = [];

    /// <summary>A new temporary directory holding an empty file per name.</summary>
    private string DirWith(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"umbraco-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(dir, file), "");
        _dirs.Add(dir);
        return dir;
    }

    private static string PathOf(params string[] dirs) => string.Join(Path.PathSeparator, dirs);

    /// <summary>The Unix rule with every existing file counted as executable.</summary>
    private static ExtensionLocator Unix(string path) => new(path, windows: false, File.Exists);

    public void Dispose()
    {
        foreach (var dir in _dirs)
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Find_WindowsExe_IsFound()
    {
        var dir = DirWith("umbraco-foo.exe");

        var found = new ExtensionLocator(PathOf(dir), windows: true).Find("foo");

        Assert.Equal(Path.Combine(dir, "umbraco-foo.exe"), found);
    }

    [Theory]
    [InlineData("umbraco-foo.cmd")]
    [InlineData("umbraco-foo.bat")]
    [InlineData("umbraco-foo.ps1")]
    [InlineData("umbraco-foo")]
    public void Find_WindowsFileThatIsNotAnExe_IsIgnored(string file)
    {
        var dir = DirWith(file);

        var found = new ExtensionLocator(PathOf(dir), windows: true).Find("foo");

        Assert.Null(found);
    }

    [Fact]
    public void Find_UnixExecutable_IsFound()
    {
        var dir = DirWith("umbraco-foo");

        var found = Unix(PathOf(dir)).Find("foo");

        Assert.Equal(Path.Combine(dir, "umbraco-foo"), found);
    }

    [Fact]
    public void Find_UnixFileWithoutAnExecuteBit_IsIgnored()
    {
        var dir = DirWith("umbraco-foo");

        var found = new ExtensionLocator(PathOf(dir), windows: false, _ => false).Find("foo");

        Assert.Null(found);
    }

    [Fact]
    public void Find_TwoPathEntries_TheFirstWins()
    {
        var first = DirWith("umbraco-foo");
        var second = DirWith("umbraco-foo");

        var found = Unix(PathOf(first, second)).Find("foo");

        Assert.Equal(Path.Combine(first, "umbraco-foo"), found);
    }

    [Fact]
    public void Find_RelativePathEntry_IsNotSearched()
    {
        // A relative entry (".", or one like this) would run whatever the current directory
        // leads to. This one really does lead to an extension, so only the rule can skip it.
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, DirWith("umbraco-foo"));
        Assert.False(Path.IsPathFullyQualified(relative), "precondition: a relative entry");

        var found = Unix(PathOf(relative)).Find("foo");

        Assert.Null(found);
    }

    [Theory]
    [InlineData("Foo")] // nouns are lower case
    [InlineData("../foo")]
    [InlineData("foo bar")]
    [InlineData("foo.exe")]
    [InlineData("-foo")]
    public void Find_WordThatIsNotANoun_IsNeverLookedUp(string word)
    {
        // The file exists under that exact name, so only the noun rule can refuse it.
        var dir = DirWith("umbraco-" + word.Replace("/", "_"));

        var found = Unix(PathOf(dir)).Find(word);

        Assert.Null(found);
    }

    [Fact]
    public void List_NamesEachNounOnceInPathOrderPrecedence()
    {
        var first = DirWith("umbraco-foo", "umbraco-bar", "unrelated");
        var second = DirWith("umbraco-foo", "umbraco-baz");

        var found = Unix(PathOf(first, second)).List();

        Assert.Equal(
            [
                new ExtensionCommand("bar", Path.Combine(first, "umbraco-bar")),
                new ExtensionCommand("baz", Path.Combine(second, "umbraco-baz")),
                new ExtensionCommand("foo", Path.Combine(first, "umbraco-foo")),
            ],
            found
        );
    }

    [Fact]
    public void List_WindowsRule_ListsOnlyExes()
    {
        var dir = DirWith("umbraco-foo.exe", "umbraco-bar.cmd", "umbraco-baz.bat");

        var found = new ExtensionLocator(PathOf(dir), windows: true).List();

        Assert.Equal(["foo"], found.Select(e => e.Noun));
    }

    [Fact]
    public void List_MissingPathDirectory_IsSkipped()
    {
        var dir = DirWith("umbraco-foo");
        var missing = Path.Combine(Path.GetTempPath(), $"umbraco-missing-{Guid.NewGuid():N}");

        var found = Unix(PathOf(missing, dir)).List();

        Assert.Equal(["foo"], found.Select(e => e.Noun));
    }
}
