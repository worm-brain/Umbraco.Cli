using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The <c>completion &lt;bash|zsh|pwsh&gt;</c> command (#92): prints a shell script that wires tab
/// completion for <c>umbraco</c> into that shell.
/// <para>
/// Every script asks the CLI itself what can come next, through System.CommandLine's built-in
/// <c>[suggest:&lt;position&gt;]</c> directive (<c>umbraco "[suggest:11]" "umbraco con"</c> prints
/// <c>content</c>), narrowed to prefix matches by <see cref="UsePrefixSuggestions"/>. So completion always matches the installed tree - nouns,
/// verbs, options and option values with a fixed set - and nothing else needs installing: no
/// <c>dotnet-suggest</c>, no generated word lists to go stale. Like <c>commands</c> it is local:
/// no host, no authentication, and it is not a resource noun (docs/conventions.md 9).
/// </para>
/// </summary>
public static class CompletionCommand
{
    /// <summary>The shells a script is available for, as the <c>shell</c> argument accepts them.</summary>
    public static readonly IReadOnlyList<string> Shells = ["bash", "zsh", "pwsh"];

    /// <summary>
    /// Makes the root's <c>[suggest]</c> directive return only the suggestions that start with
    /// the word being completed, ignoring case (#398). System.CommandLine's own directive matches
    /// the word anywhere in a name, so <c>con</c> also offered <c>--config</c>; the shell scripts
    /// filter by prefix themselves (#379), but any other caller of the directive got the extras.
    /// </summary>
    /// <param name="root">The root command whose suggest directive is replaced.</param>
    public static void UsePrefixSuggestions(RootCommand root)
    {
        foreach (var directive in root.Directives.OfType<SuggestDirective>())
            directive.Action = new PrefixSuggestAction(directive);
    }

    /// <summary>
    /// The <c>[suggest:&lt;position&gt;]</c> action, mirroring System.CommandLine's built-in one
    /// (the last non-directive token is the command line, the directive value is the cursor
    /// position in it) with a prefix filter on top of its substring matching.
    /// </summary>
    /// <param name="directive">The suggest directive this action serves, for its position value.</param>
    private sealed class PrefixSuggestAction(SuggestDirective directive)
        : SynchronousCommandLineAction
    {
        /// <summary>
        /// True, as for the built-in action: the line to complete is one argument the root does
        /// not recognise, so without this the invocation is a parse error and <c>Program.cs</c>
        /// reports it instead of printing suggestions.
        /// </summary>
        public override bool ClearsParseErrors => true;

        /// <summary>Prints the prefix-matching suggestions, one per line.</summary>
        /// <param name="parseResult">The parse of the directive invocation itself.</param>
        /// <returns>Always 0.</returns>
        public override int Invoke(ParseResult parseResult)
        {
            // The line to complete is passed as one argument after the directive; an absent
            // position means "at the end of it", as in the built-in action.
            var line =
                parseResult.Tokens.LastOrDefault(t => t.Type != TokenType.Directive)?.Value ?? "";
            var positionValue = parseResult.GetResult(directive)?.Values.SingleOrDefault();
            var position = int.TryParse(positionValue, out var p) ? p : line.Length;

            var lineParse = parseResult.RootCommandResult.Command.Parse(
                line,
                parseResult.Configuration
            );
            var word = lineParse.GetCompletionContext() is TextCompletionContext text
                ? text.AtCursorPosition(position).WordToComplete
                : "";

            var suggestions = lineParse
                .GetCompletions(position)
                .Where(c => c.Label.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Label);
            parseResult.InvocationConfiguration.Output.WriteLine(
                string.Join(Environment.NewLine, suggestions)
            );
            return 0;
        }
    }

    /// <summary>Builds the <c>completion</c> command.</summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var cmd = new Command(
            "completion",
            "Print a tab-completion script for bash, zsh or PowerShell.\n\n"
                + "Load it from your shell's startup file; it asks the installed CLI for "
                + "suggestions, so it never goes stale."
        ).WithExamples(
            "umbraco completion bash > ~/.umbraco-completion.bash   # then source it from ~/.bashrc",
            "umbraco completion zsh > ~/.zfunc/_umbraco           # a directory on $fpath",
            "umbraco completion pwsh | Out-String | Invoke-Expression"
        );
        var shellArg = new Argument<string>("shell")
        {
            Description = "The shell to print the script for: bash, zsh or pwsh.",
        };
        shellArg.AcceptOnlyFromAmong([.. Shells]);
        cmd.Add(shellArg);

        cmd.SetAction(
            (parseResult, _) =>
            {
                // A bare script on stdout, never the JSON envelope: it is sourced by a shell.
                Console.Out.Write(ScriptFor(parseResult.GetValue(shellArg)!));
                return Task.FromResult(0);
            }
        );
        return cmd;
    }

    /// <summary>
    /// The completion script for a shell, with LF line endings whatever the build OS (#375). The
    /// scripts are raw string literals, so they carry the line endings of the checkout the tool
    /// was built from, and a CR left in them is a syntax error to zsh and to bash on Linux.
    /// </summary>
    /// <param name="shell">One of <see cref="Shells"/>.</param>
    /// <returns>The script text, ending in a newline.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shell"/> is not a supported shell.</exception>
    public static string ScriptFor(string shell) =>
        (
            shell switch
            {
                "bash" => Bash,
                "zsh" => Zsh,
                "pwsh" => Pwsh,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(shell),
                    shell,
                    "Supported shells: " + string.Join(", ", Shells) + "."
                ),
            }
        ).ReplaceLineEndings("\n");

    // Every script runs the command the user typed (its first word), not whatever `umbraco` is
    // first on PATH (#379), so `./umbraco`, a path to a build, or a wrapper answers for itself.
    // The line it sends has that word replaced by `umbraco`, so the CLI's parser recognises the
    // root command however it was typed (`umbraco.exe`, `~/bin/umbraco`, a wrapper's name), and
    // the cursor offset moves with it.

    // bash: COMP_LINE/COMP_POINT give the line up to the cursor, which is exactly what the
    // directive wants (the text and the cursor offset in it). compgen -W filters by the word being
    // completed. tr strips the CR a Windows build prints, so Git Bash gets clean words. -o default
    // falls back to file names when the CLI has no suggestion (a --json-body path, say).
    private const string Bash = """
        # umbraco bash completion. Load it from ~/.bashrc:
        #   eval "$(umbraco completion bash)"
        # To complete a wrapper script too: complete -o default -F _umbraco_complete <name>
        _umbraco_complete() {
          local cmd="${COMP_WORDS[0]}"
          local line="${COMP_LINE:0:COMP_POINT}"
          line="umbraco${line#*"$cmd"}"
          local IFS=$'\n'
          COMPREPLY=($(compgen -W "$("${cmd/#\~/$HOME}" "[suggest:${#line}]" "$line" 2>/dev/null | tr -d '\r')" -- "${COMP_WORDS[COMP_CWORD]}"))
        }
        complete -o default -F _umbraco_complete umbraco

        """;

    // zsh: $words[2,CURRENT] is the command line after the command, up to the word being
    // completed, and joining it with spaces keeps the trailing empty word, so
    // "umbraco content <TAB>" asks for what follows "content". compadd filters by the prefix. The
    // last lines make one file work both ways: autoloaded from $fpath (where the file body runs
    // as _umbraco itself) or sourced/eval'd after compinit (which registers it).
    private const string Zsh = """
        #compdef umbraco
        # umbraco zsh completion. Save it as _umbraco in a directory on $fpath, or load it from
        # ~/.zshrc after compinit:
        #   eval "$(umbraco completion zsh)"
        # To complete a wrapper script too: compdef _umbraco <name>
        _umbraco() {
          local cmd="${${(Q)words[1]}/#\~/$HOME}"
          local line="umbraco ${words[2,CURRENT]}"
          local -a suggestions
          suggestions=("${(@f)$("$cmd" "[suggest:${#line}]" "$line" 2>/dev/null | tr -d '\r')}")
          compadd -a suggestions
        }
        if [[ "${funcstack[1]}" == "_umbraco" ]]; then
          _umbraco "$@"
        else
          compdef _umbraco umbraco
        fi

        """;

    // PowerShell: the cursor position is relative to the whole input line, so it is made relative
    // to this command's own text, which is padded when the cursor sits after a trailing space.
    // Unlike bash and zsh, PowerShell shows whatever the completer returns, so the results are
    // filtered by prefix here too. The CLI already returns prefix matches only (#398); the filter
    // stays so the script also works against an older CLI that matched anywhere in a name.
    private const string Pwsh = """
        # umbraco PowerShell completion. Load it from your profile:
        #   umbraco completion pwsh | Out-String | Invoke-Expression
        Register-ArgumentCompleter -Native -CommandName umbraco, umbraco.exe -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $start = $commandAst.Extent.StartOffset
            $line = $commandAst.ToString()
            $position = $cursorPosition - $start
            if ($position -gt $line.Length) { $line = $line.PadRight($position) }
            $typed = $commandAst.CommandElements[0].Extent.EndOffset - $start
            $line = 'umbraco' + $line.Substring($typed)
            $position = $position - $typed + 'umbraco'.Length
            & $commandAst.GetCommandName() "[suggest:$position]" "$line" 2>$null |
                Where-Object { $_.StartsWith($wordToComplete, [System.StringComparison]::OrdinalIgnoreCase) } |
                ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
        }

        """;
}
