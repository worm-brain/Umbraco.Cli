using System.CommandLine;
using System.Text.RegularExpressions;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Rule 8.5 of docs/conventions.md: examples that run (#250 cause 7). Help and doc examples were
/// never executed, so they drifted from the parser - renamed options, GUID-typed arguments given a
/// name, invented event aliases. These tests parse every example the shipped tree declares (the list
/// each command holds via <see cref="CommandExamples.WithExamples{TCommand}"/>, #276) with the
/// production parser, and check that every command and option named in <c>docs/commands.md</c>
/// exists. Placeholders come from the closed vocabulary in <see cref="ExamplePlaceholders"/>.
/// </summary>
public partial class HelpTextTests
{
    /// <summary>The GUID a truncated id or an id placeholder stands for.</summary>
    private const string SampleId = ExamplePlaceholders.SampleId;

    /// <summary>The real tree to parse against, built once: parsing does not change it.</summary>
    private static readonly RootCommand Root = TestCliRoot.Build();

    [Fact]
    public void EveryHelpExample_ParsesWithTheRealParser()
    {
        AssertNone(
            HelpExamples()
                .SelectMany(e => UmbracoSegments(e.Line).Select(s => (e.Path, e.Line, Segment: s)))
                .Select(e =>
                    (e.Path, e.Line, Args: Substitute(e.Segment, out var unknown), unknown)
                )
                .Select(e =>
                    e.unknown.Count > 0
                        ? $"{e.Path}: `{e.Line}` uses unknown placeholder(s) {string.Join(", ", e.unknown)}"
                    : Root.Parse(e.Args, CliParserConfiguration.Create()).Errors
                        is { Count: > 0 } errors
                        ? $"{e.Path}: `{e.Line}` -> {string.Join("; ", errors.Select(x => x.Message))}"
                    : null
                )
                .OfType<string>(),
            "Help example that does not parse"
        );
    }

    [Fact]
    public void EveryHelpExample_IsAnUmbracoCommandOrAComment()
    {
        // A line with no umbraco command in it is prose that slipped into the block, or a
        // command for some other tool, and would otherwise pass unchecked.
        AssertNone(
            HelpExamples()
                .Where(e => !e.Line.StartsWith('#') && UmbracoSegments(e.Line).Count == 0)
                .Select(e => $"{e.Path}: `{e.Line}`"),
            "Example line with no umbraco command"
        );
    }

    [Fact]
    public void EveryCommandsDocLine_NamesARealCommandAndRealOptions()
    {
        // docs/commands.md lines are synopses ([--opt <x>], <id|alias>, ...), not runnable, so
        // this checks the parts that go stale on a rename: the command path and the option names.
        AssertNone(
            CommandsDocLines()
                .SelectMany(line => DocSegments(line).SelectMany(s => DocProblems(line, s))),
            "docs/commands.md line naming a missing command or option"
        );
    }

    [Fact]
    public void HelpExamples_AreFound()
    {
        // Guards the tests above against passing vacuously if examples stop reaching the catalog.
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
    /// Every example line in the tree, with the command it belongs to, read from the list each
    /// command declared (the catalog's <c>examples</c>) rather than cut out of its help text.
    /// </summary>
    private static IEnumerable<(string Path, string Line)> HelpExamples() =>
        Commands().SelectMany(c => (c.Node.Examples ?? []).Select(line => (c.Path, line)));

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
        var args = TruncatedId().Replace(segment["umbraco ".Length..], SampleId);

        var missing = new List<string>();
        args = AnglePlaceholder()
            .Replace(
                args,
                m =>
                {
                    // Anything in angle brackets outside the vocabulary is reported, not guessed at.
                    if (ExamplePlaceholders.Values.TryGetValue(m.Value, out var value))
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
            var raw in File.ReadLines(Path.Combine(TestPaths.RepoRoot(), "docs", "commands.md"))
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

    /// <summary>
    /// What is wrong with one synopsis segment: a bare word straight after a group (a subcommand
    /// that does not exist), or an option the resolved command does not have.
    /// </summary>
    /// <param name="line">The whole line, for the message.</param>
    /// <param name="segment">One umbraco segment of it.</param>
    /// <returns>One message per problem; empty when the segment is sound.</returns>
    private static IEnumerable<string> DocProblems(string line, string segment)
    {
        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        var chain = Resolve(tokens);
        var command = chain[^1];
        var path = string.Join(' ', chain.Skip(1).Select(c => c.Name).Prepend("umbraco"));
        var rest = tokens.Skip(chain.Count - 1).ToList();

        if (
            command.Subcommands.Count > 0
            && rest.FirstOrDefault() is { } next
            && BareWord().IsMatch(next)
        )
            return [$"`{line}`: no command '{next}' under '{path}'"];

        var valid = OptionNames(chain);
        return rest.Select(OptionToken)
            .OfType<string>()
            .Where(o => !valid.Contains(o))
            .Select(o => $"`{line}`: '{path}' has no option {o}");
    }

    /// <summary>Walks the tokens down the tree for as long as each names a subcommand.</summary>
    /// <param name="tokens">The tokens after <c>umbraco</c>.</param>
    /// <returns>The commands walked through, root first; the last is the one the tokens name.</returns>
    private static List<Command> Resolve(List<string> tokens)
    {
        var chain = new List<Command> { Root };
        foreach (var token in tokens)
        {
            if (
                chain[^1]
                    .Subcommands.FirstOrDefault(c => c.Name == token || c.Aliases.Contains(token))
                is not { } child
            )
                break;
            chain.Add(child);
        }
        return chain;
    }

    /// <summary>
    /// Every option name valid on the last command of <paramref name="chain"/>: its own options and
    /// aliases, plus the recursive (global) options of every ancestor.
    /// </summary>
    /// <param name="chain">The commands from the root down, as <see cref="Resolve"/> returns them.</param>
    /// <returns>The option names, e.g. <c>--parent</c>, <c>-o</c>.</returns>
    private static HashSet<string> OptionNames(List<Command> chain) =>
        chain
            .SelectMany((c, i) => c.Options.Where(o => i == chain.Count - 1 || o.Recursive))
            .SelectMany(o => o.Aliases.Prepend(o.Name))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The option name at the start of a synopsis token (after any <c>[</c> or <c>(</c>).</summary>
    /// <param name="token">A token such as <c>[--parent</c> or <c>(--order</c>.</param>
    /// <returns>The option name, or null when the token is not an option.</returns>
    private static string? OptionToken(string token) =>
        OptionName().Match(token.TrimStart('[', '(')) is { Success: true } m ? m.Value : null;

    /// <summary>A trailing shell comment: whitespace, <c>#</c>, then anything.</summary>
    [GeneratedRegex(@"\s+#.*$")]
    private static partial Regex ShellComment();

    /// <summary>An output redirect to the end of the segment.</summary>
    [GeneratedRegex(@"\s+>\s*\S+.*$")]
    private static partial Regex Redirect();

    /// <summary>A truncated id, as <see cref="ExamplePlaceholders.TruncatedIdPattern"/> defines it.</summary>
    [GeneratedRegex(ExamplePlaceholders.TruncatedIdPattern)]
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
