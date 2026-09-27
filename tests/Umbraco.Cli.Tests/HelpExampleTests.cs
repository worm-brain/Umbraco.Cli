using System.CommandLine;
using System.Text.RegularExpressions;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Examples that run (#250 cause 7). Help and doc examples were never executed, so they drifted
/// from the parser: renamed options, GUID-typed arguments given a name, invented event aliases.
/// These tests parse every <c>Examples:</c> line in the shipped tree with the production parser,
/// and check that every command and option named in <c>docs/commands.md</c> exists. Placeholders
/// come from the closed vocabulary in docs/conventions.md section 8.
/// </summary>
public partial class HelpExampleTests
{
    /// <summary>The GUID a truncated id or an id placeholder stands for.</summary>
    private const string SampleId = "3f7a8b2e-1234-5678-abcd-ef0123456789";

    /// <summary>
    /// The placeholders an example may use (docs/conventions.md 8.5), and what each parses as.
    /// Anything else in angle brackets fails the test rather than being guessed at.
    /// </summary>
    private static readonly Dictionary<string, string> Placeholders = new()
    {
        ["<id>"] = SampleId,
        ["<guid>"] = SampleId,
        ["<folder-id>"] = SampleId,
        ["<version-id>"] = SampleId,
        ["<relation-type-id>"] = SampleId,
        ["<section-id>"] = SampleId,
        ["<secret>"] = "s3cret",
    };

    /// <summary>The real tree, built once: parsing does not change it.</summary>
    private static readonly RootCommand Root = TestCliRoot.Build();

    [Fact]
    public void EveryHelpExample_ParsesWithTheRealParser()
    {
        var failures = new List<string>();
        foreach (var (path, line) in HelpExamples())
        {
            foreach (var segment in UmbracoSegments(line))
            {
                var args = Substitute(segment, out var unknown);
                if (unknown.Count > 0)
                {
                    failures.Add(
                        $"{path}: `{line}` uses unknown placeholder(s) {string.Join(", ", unknown)}"
                    );
                    continue;
                }

                var errors = Root.Parse(args, CliParserConfiguration.Create()).Errors;
                if (errors.Count > 0)
                    failures.Add(
                        $"{path}: `{line}` -> {string.Join("; ", errors.Select(e => e.Message))}"
                    );
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Help examples that do not parse ({failures.Count}):\n  "
                + string.Join("\n  ", failures)
        );
    }

    [Fact]
    public void EveryHelpExample_IsAnUmbracoCommandOrAComment()
    {
        // A line with no umbraco command in it is prose that slipped into the block, or a
        // command for some other tool, and would otherwise pass unchecked.
        var failures = HelpExamples()
            .Where(e => !e.Line.StartsWith('#') && !UmbracoSegments(e.Line).Any())
            .Select(e => $"{e.Path}: `{e.Line}`")
            .ToList();

        Assert.True(
            failures.Count == 0,
            $"Example lines with no umbraco command ({failures.Count}):\n  "
                + string.Join("\n  ", failures)
        );
    }

    [Fact]
    public void EveryCommandsDocLine_NamesARealCommandAndRealOptions()
    {
        // docs/commands.md lines are synopses ([--opt <x>], <id|alias>, ...), not runnable, so
        // this checks the parts that go stale on a rename: the command path and the option names.
        var failures = new List<string>();
        foreach (var line in CommandsDocLines())
        foreach (var segment in DocSegments(line))
        {
            var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
            var (command, consumed) = Resolve(tokens);
            var next = tokens.Skip(consumed).FirstOrDefault();

            // A bare word straight after a group is a subcommand that does not exist.
            if (command.Subcommands.Count > 0 && next is not null && IsBareWord(next))
            {
                failures.Add($"`{line}`: no command '{next}' under '{Path(tokens, consumed)}'");
                continue;
            }

            var valid = OptionNames(tokens, consumed);
            foreach (var option in tokens.Skip(consumed).Select(OptionToken).OfType<string>())
                if (!valid.Contains(option))
                    failures.Add($"`{line}`: '{Path(tokens, consumed)}' has no option {option}");
        }

        Assert.True(
            failures.Count == 0,
            $"docs/commands.md lines naming a missing command or option ({failures.Count}):\n  "
                + string.Join("\n  ", failures)
        );
    }

    [Fact]
    public void HelpExamples_AreFound()
    {
        // Guards the tests above against passing vacuously if the Examples: layout changes.
        Assert.True(HelpExamples().Count() > 300, "Expected the tree's 400-odd example lines.");
    }

    [Fact]
    public void CommandsDocLines_AreFound()
    {
        // The same guard for docs/commands.md, if its fences change from ```bash.
        Assert.True(CommandsDocLines().Count() > 150, "Expected 200-odd umbraco synopsis lines.");
    }

    [Theory]
    [InlineData("umbraco content get 3f7a8b2e-...", "content get " + SampleId)]
    [InlineData("umbraco content delete <id> --yes", "content delete " + SampleId + " --yes")]
    [InlineData("umbraco auth login --client-secret <secret>", "auth login --client-secret s3cret")]
    public void Substitute_KnownPlaceholder_BecomesAParseableValue(string example, string expected)
    {
        Assert.Equal(expected, Substitute(example, out _));
    }

    [Fact]
    public void Substitute_UnknownPlaceholder_IsReported()
    {
        Substitute("umbraco content get <thing>", out var unknown);

        Assert.Equal(["<thing>"], unknown);
    }

    [Theory]
    [InlineData(
        "umbraco content get <id> -o json | jq .data.values",
        "umbraco content get <id> -o json"
    )]
    [InlineData(
        "umbraco content move 3f7a8b2e-...   # to the root",
        "umbraco content move 3f7a8b2e-..."
    )]
    [InlineData(
        "umbraco document-type get t -o json | jq .data > t.json",
        "umbraco document-type get t -o json"
    )]
    [InlineData(
        "cat ids.txt | umbraco content bulk delete --yes",
        "umbraco content bulk delete --yes"
    )]
    public void UmbracoSegments_ShellLine_KeepsOnlyTheUmbracoCommand(string line, string expected)
    {
        Assert.Equal([expected], UmbracoSegments(line));
    }

    // ── Help examples ────────────────────────────────────────────────────────

    /// <summary>
    /// Every example line in the tree, with the command it belongs to. An <c>Examples:</c> block is
    /// the run of two-space-indented lines after the heading; prose may follow a blank line.
    /// </summary>
    private static IEnumerable<(string Path, string Line)> HelpExamples()
    {
        return Walk(CommandCatalog.Describe(Root), "umbraco");

        static IEnumerable<(string, string)> Walk(CommandCatalogNode node, string path)
        {
            var description = (node.Description ?? "").Replace("\r\n", "\n");
            var start = description.IndexOf("\nExamples:\n", StringComparison.Ordinal);
            if (start >= 0)
                foreach (
                    var line in description[(start + "\nExamples:\n".Length)..]
                        .Split('\n')
                        .TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal))
                )
                    yield return (path, line.Trim());

            foreach (var child in node.Commands)
            foreach (var example in Walk(child, $"{path} {child.Name}"))
                yield return example;
        }
    }

    /// <summary>
    /// The <c>umbraco</c> commands in a shell line: a trailing comment and any redirect are cut,
    /// the line is split on pipes, and only segments that start with <c>umbraco</c> are kept.
    /// </summary>
    /// <param name="line">One example line.</param>
    /// <returns>The umbraco command segments, trimmed.</returns>
    private static List<string> UmbracoSegments(string line) =>
        [
            .. ShellComment()
                .Replace(line, "")
                .Split(" | ")
                .Select(s => Redirect().Replace(s, "").Trim())
                .Where(s => s.StartsWith("umbraco ", StringComparison.Ordinal)),
        ];

    /// <summary>
    /// Turns an example command into parser input: drops the leading <c>umbraco</c> (the test
    /// root has no name) and replaces each placeholder with the value it stands for.
    /// </summary>
    /// <param name="segment">An umbraco command from an example.</param>
    /// <param name="unknown">Angle-bracket placeholders not in the vocabulary.</param>
    /// <returns>The arguments to parse.</returns>
    private static string Substitute(string segment, out List<string> unknown)
    {
        var args = segment["umbraco ".Length..];
        args = TruncatedId().Replace(args, SampleId);

        var missing = new List<string>();
        args = AnglePlaceholder()
            .Replace(
                args,
                m =>
                {
                    if (Placeholders.TryGetValue(m.Value, out var value))
                        return value;
                    missing.Add(m.Value);
                    return m.Value;
                }
            );
        unknown = missing;
        return args;
    }

    // ── docs/commands.md ─────────────────────────────────────────────────────

    /// <summary>Every <c>umbraco</c> line inside a <c>```bash</c> fence of docs/commands.md.</summary>
    private static IEnumerable<string> CommandsDocLines()
    {
        var inBash = false;
        foreach (
            var raw in File.ReadLines(
                System.IO.Path.Combine(SurfaceSnapshotTests.RepoRoot(), "docs", "commands.md")
            )
        )
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
                inBash = line == "```bash";
            else if (inBash && line.StartsWith("umbraco ", StringComparison.Ordinal))
                yield return line;
        }
    }

    /// <summary>
    /// The umbraco commands in a synopsis line. A comment is cut, and the line is split on a pipe
    /// only where one feeds another program: <c>--schema | --example</c> is an "or", not a pipe.
    /// </summary>
    /// <param name="line">One synopsis line.</param>
    /// <returns>The umbraco segments.</returns>
    private static IEnumerable<string> DocSegments(string line) =>
        DocPipe()
            .Split(ShellComment().Replace(line, ""))
            .Select(s => s.Trim())
            .Where(s => s.StartsWith("umbraco ", StringComparison.Ordinal));

    /// <summary>Walks the tokens down the tree for as long as each names a subcommand.</summary>
    /// <param name="tokens">The tokens after <c>umbraco</c>.</param>
    /// <returns>The deepest command reached, and how many tokens named it.</returns>
    private static (Command Command, int Consumed) Resolve(List<string> tokens)
    {
        Command command = Root;
        var consumed = 0;
        while (
            consumed < tokens.Count
            && command.Subcommands.FirstOrDefault(c =>
                c.Name == tokens[consumed] || c.Aliases.Contains(tokens[consumed])
            )
                is { } child
        )
        {
            command = child;
            consumed++;
        }
        return (command, consumed);
    }

    /// <summary>
    /// Every option name valid on the command the tokens resolve to: its own options and aliases,
    /// plus the recursive (global) options of every ancestor.
    /// </summary>
    /// <param name="tokens">The tokens after <c>umbraco</c>.</param>
    /// <param name="consumed">How many of them name the command.</param>
    /// <returns>The option names, e.g. <c>--parent</c>, <c>-o</c>.</returns>
    private static HashSet<string> OptionNames(List<string> tokens, int consumed)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Command command = Root;
        for (var i = 0; i <= consumed; i++)
        {
            var own = i == consumed;
            foreach (var option in command.Options.Where(o => own || o.Recursive))
            {
                names.Add(option.Name);
                names.UnionWith(option.Aliases);
            }
            if (i < consumed)
                command = command.Subcommands.First(c =>
                    c.Name == tokens[i] || c.Aliases.Contains(tokens[i])
                );
        }
        return names;
    }

    /// <summary>The option name at the start of a synopsis token (after any <c>[</c> or <c>(</c>).</summary>
    /// <param name="token">A token such as <c>[--parent</c> or <c>(--order</c>.</param>
    /// <returns>The option name, or null when the token is not an option.</returns>
    private static string? OptionToken(string token) =>
        OptionName().Match(token.TrimStart('[', '(')) is { Success: true } m ? m.Value : null;

    /// <summary>Whether a token is a plain word (a would-be subcommand), not an option, placeholder or value.</summary>
    /// <param name="token">The token.</param>
    /// <returns>True for a lower-case word such as <c>list</c>.</returns>
    private static bool IsBareWord(string token) => BareWord().IsMatch(token);

    /// <summary>The command path the first <paramref name="consumed"/> tokens name, for messages.</summary>
    /// <param name="tokens">The tokens after <c>umbraco</c>.</param>
    /// <param name="consumed">How many name the command.</param>
    /// <returns>E.g. <c>umbraco content version</c>.</returns>
    private static string Path(List<string> tokens, int consumed) =>
        string.Join(' ', tokens.Take(consumed).Prepend("umbraco"));

    /// <summary>A trailing shell comment: whitespace, <c>#</c>, then anything.</summary>
    [GeneratedRegex(@"\s+#.*$")]
    private static partial Regex ShellComment();

    /// <summary>An output redirect to the end of the segment.</summary>
    [GeneratedRegex(@"\s+>\s*\S+.*$")]
    private static partial Regex Redirect();

    /// <summary>A truncated id: eight hex digits, a dash and an ellipsis (<c>3f7a8b2e-...</c>).</summary>
    [GeneratedRegex(@"\b[0-9a-f]{8}-\.\.\.")]
    private static partial Regex TruncatedId();

    /// <summary>An angle-bracket placeholder such as <c>&lt;id&gt;</c>.</summary>
    [GeneratedRegex(@"<[^<>\s]+>")]
    private static partial Regex AnglePlaceholder();

    /// <summary>A pipe into another program (the next word does not start with <c>-</c>).</summary>
    [GeneratedRegex(@"\s\|\s(?=[^-\s])")]
    private static partial Regex DocPipe();

    /// <summary>An option name: one or two dashes and a letter, then word characters or dashes.</summary>
    [GeneratedRegex(@"^--?[A-Za-z][\w-]*")]
    private static partial Regex OptionName();

    /// <summary>A lower-case word, the shape of a subcommand name.</summary>
    [GeneratedRegex(@"^[a-z][a-z-]*$")]
    private static partial Regex BareWord();
}
