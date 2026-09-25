using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Names renamed by #268 keep working for one release: the command line is rewritten to the
/// current name with a warning, and allow-list entries written against the old names still match.
/// </summary>
public class LegacyNamesTests
{
    [Fact]
    public void TryRewrite_OldTopLevelNoun_UsesTheNewNameAndWarns()
    {
        string[] args = ["content-types", "list", "-o", "json"];
        var parsed = TestCliRoot.Build().Parse(args);

        var rewrote = LegacyNames.TryRewrite(parsed, args, out var rewritten, out var warning);

        Assert.True(rewrote);
        Assert.Equal(["document-type", "list", "-o", "json"], rewritten);
        Assert.Contains("'content-types' is now 'document-type'", warning);
    }

    [Fact]
    public void TryRewrite_OldSubNoun_UsesTheNewName()
    {
        string[] args = ["content", "domains", "get", "3f7a8b2e-1234-5678-abcd-ef0123456789"];
        var parsed = TestCliRoot.Build().Parse(args);

        LegacyNames.TryRewrite(parsed, args, out var rewritten, out _);

        Assert.Equal("domain", rewritten[1]);
    }

    [Fact]
    public void TryRewrite_CurrentName_LeavesTheCommandLineAlone()
    {
        string[] args = ["document-type", "list"];
        var parsed = TestCliRoot.Build().Parse(args);

        var rewrote = LegacyNames.TryRewrite(parsed, args, out _, out _);

        Assert.False(rewrote);
    }

    [Fact]
    public void TryRewrite_UnknownName_IsLeftForTheParseError()
    {
        // Only names in the rename table are rewritten; a typo still gets the normal parse error.
        string[] args = ["content-typez", "list"];
        var parsed = TestCliRoot.Build().Parse(args);

        var rewrote = LegacyNames.TryRewrite(parsed, args, out _, out _);

        Assert.False(rewrote);
    }

    [Theory]
    [InlineData("content-types", "document-type")]
    [InlineData("user-groups.delete", "user-group.delete")]
    [InlineData("content.domains.set", "content.domain.set")]
    [InlineData("content", "content")]
    public void Canonical_AllowListEntry_NamesTheSameCommandsUnderTheCurrentNames(
        string entry,
        string expected
    )
    {
        var canonical = LegacyNames.Canonical(entry);

        Assert.Equal(expected, canonical);
    }
}
