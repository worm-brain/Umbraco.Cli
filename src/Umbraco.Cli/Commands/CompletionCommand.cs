using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The <c>completion &lt;bash|zsh|pwsh&gt;</c> command (#92): prints a shell script that wires tab
/// completion for <c>umbraco</c> into that shell.
/// <para>
/// Every script asks the CLI itself what can come next, through System.CommandLine's built-in
/// <c>[suggest:&lt;position&gt;]</c> directive (<c>umbraco "[suggest:11]" "umbraco con"</c> prints
/// <c>content</c> and <c>--config</c>). So completion always matches the installed tree - nouns,
/// verbs, options and option values with a fixed set - and nothing else needs installing: no
/// <c>dotnet-suggest</c>, no generated word lists to go stale. Like <c>commands</c> it is local:
/// no host, no authentication, and it is not a resource noun (docs/conventions.md 9).
/// </para>
/// </summary>
public static class CompletionCommand
{
    /// <summary>The shells a script is available for, as the <c>shell</c> argument accepts them.</summary>
    public static readonly IReadOnlyList<string> Shells = ["bash", "zsh", "pwsh"];

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

    /// <summary>The completion script for a shell.</summary>
    /// <param name="shell">One of <see cref="Shells"/>.</param>
    /// <returns>The script text, ending in a newline.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shell"/> is not a supported shell.</exception>
    public static string ScriptFor(string shell) =>
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
        };

    // bash: COMP_LINE/COMP_POINT give the line up to the cursor, which is exactly what the
    // directive wants (the text and the cursor offset in it). tr strips the CR a Windows build
    // prints, so Git Bash gets clean words. -o default falls back to file names when the CLI has
    // no suggestion (a --json-body path, say).
    private const string Bash = """
        # umbraco bash completion. Load it from ~/.bashrc:
        #   eval "$(umbraco completion bash)"
        _umbraco_complete() {
          local line="${COMP_LINE:0:COMP_POINT}"
          local IFS=$'\n'
          COMPREPLY=($(compgen -W "$(umbraco "[suggest:${#line}]" "$line" 2>/dev/null | tr -d '\r')" -- "${COMP_WORDS[COMP_CWORD]}"))
        }
        complete -o default -F _umbraco_complete umbraco

        """;

    // zsh: $words[1,CURRENT] is the command line up to the word being completed, and joining it
    // with spaces keeps the trailing empty word, so "umbraco content <TAB>" asks for what follows
    // "content". The last lines make one file work both ways: autoloaded from $fpath (where the
    // file body runs as _umbraco itself) or sourced/eval'd after compinit (which registers it).
    private const string Zsh = """
        #compdef umbraco
        # umbraco zsh completion. Save it as _umbraco in a directory on $fpath, or load it from
        # ~/.zshrc after compinit:
        #   eval "$(umbraco completion zsh)"
        _umbraco() {
          local line="${words[1,CURRENT]}"
          local -a suggestions
          suggestions=("${(@f)$(umbraco "[suggest:${#line}]" "$line" 2>/dev/null | tr -d '\r')}")
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
    private const string Pwsh = """
        # umbraco PowerShell completion. Load it from your profile:
        #   umbraco completion pwsh | Out-String | Invoke-Expression
        Register-ArgumentCompleter -Native -CommandName umbraco, umbraco.exe -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $line = $commandAst.ToString()
            $position = $cursorPosition - $commandAst.Extent.StartOffset
            if ($position -gt $line.Length) { $line = $line.PadRight($position) }
            umbraco "[suggest:$position]" "$line" 2>$null | ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        }

        """;
}
