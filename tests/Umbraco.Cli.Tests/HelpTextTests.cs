using System.Text.RegularExpressions;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The help-text rules of docs/conventions.md section 8, checked over the whole shipped tree so a
/// new command cannot quietly break them. Each test lists every violation at once.
/// </summary>
public partial class HelpTextTests
{
    /// <summary>Every command in the tree, with its space-separated path (the root is "umbraco").</summary>
    private static IEnumerable<(string Path, CommandCatalogNode Node)> Commands()
    {
        var root = CommandCatalog.Describe(TestCliRoot.Build());
        return Walk(root, "umbraco");

        static IEnumerable<(string, CommandCatalogNode)> Walk(
            CommandCatalogNode node,
            string path
        ) => node.Commands.SelectMany(c => Walk(c, $"{path} {c.Name}")).Prepend((path, node));
    }

    private static string Summary(CommandCatalogNode node) =>
        (node.Description ?? "").Split('\n')[0].Trim();

    private static void AssertNone(IEnumerable<string> violations, string rule)
    {
        var list = violations.ToList();
        Assert.True(list.Count == 0, $"{rule} ({list.Count}):\n  " + string.Join("\n  ", list));
    }

    [Fact]
    public void Summaries_AreAtMost80CharactersAndEndWithAFullStop()
    {
        AssertNone(
            Commands()
                .Where(c => Summary(c.Node) is var s && (s.Length > 80 || !s.EndsWith('.')))
                .Select(c => $"{c.Path}: \"{Summary(c.Node)}\""),
            "Summary longer than 80 characters or without a full stop"
        );
    }

    [Fact]
    public void Summaries_StartWithACapital()
    {
        AssertNone(
            Commands()
                .Where(c => Summary(c.Node) is not { Length: > 0 } s || !char.IsUpper(s[0]))
                .Select(c => c.Path),
            "Summary missing or not capitalised"
        );
    }

    [Fact]
    public void EveryOptionAndArgument_HasHelpText()
    {
        AssertNone(
            Commands()
                .SelectMany(c =>
                    c.Node.Options.Where(o => string.IsNullOrWhiteSpace(o.Description))
                        .Select(o => $"{c.Path} {o.Name}")
                        .Concat(
                            c.Node.Arguments.Where(a => string.IsNullOrWhiteSpace(a.Description))
                                .Select(a => $"{c.Path} <{a.Name}>")
                        )
                ),
            "Option or argument without help text"
        );
    }

    [Fact]
    public void EveryLeaf_HasAnExamplesBlock()
    {
        AssertNone(
            Commands()
                .Where(c =>
                    c.Node.Commands.Count == 0
                    && !(c.Node.Description ?? "").Contains("\nExamples:\n")
                )
                .Select(c => c.Path),
            "Leaf command without an \"Examples:\" block"
        );
    }

    [Fact]
    public void HelpText_CitesNoIssueNumbers()
    {
        AssertNone(
            AllHelp().Where(h => IssueNumber().IsMatch(h.Text)).Select(h => h.Where),
            "Help text citing an issue number"
        );
    }

    [Theory]
    [InlineData("UUID", "id")]
    [InlineData("back-office", "backoffice")]
    [InlineData("content type", "document type")]
    public void HelpText_UsesOneTermPerConcept(string avoid, string use)
    {
        AssertNone(
            AllHelp()
                .Where(h => h.Text.Contains(avoid, StringComparison.OrdinalIgnoreCase))
                .Select(h => h.Where),
            $"Help text saying \"{avoid}\" instead of \"{use}\""
        );
    }

    /// <summary>Every piece of help text in the tree, with where it is.</summary>
    private static IEnumerable<(string Where, string Text)> AllHelp() =>
        Commands()
            .SelectMany(c =>
                new[] { (c.Path, c.Node.Description ?? "") }
                    .Concat(c.Node.Options.Select(o => ($"{c.Path} {o.Name}", o.Description ?? "")))
                    .Concat(
                        c.Node.Arguments.Select(a => ($"{c.Path} <{a.Name}>", a.Description ?? ""))
                    )
            );

    [GeneratedRegex(@"\(#\d+\)|#\d{2,}")]
    private static partial Regex IssueNumber();
}
