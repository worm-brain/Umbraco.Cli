using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>umbraco completion &lt;shell&gt;</c> (#92) and the <c>[suggest]</c> directive its scripts
/// call.
/// </summary>
[Collection("ConsoleCapture")]
public class CompletionCommandTests
{
    /// <summary>Invokes the shipped tree with <paramref name="args"/> and captures stdout.</summary>
    /// <param name="args">The arguments, as a shell would pass them.</param>
    /// <returns>The exit code and stdout.</returns>
    private static async Task<(int Exit, string Out)> Run(params string[] args)
    {
        var original = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var exit = await TestCliRoot.Build().Parse(args).InvokeAsync();
            return (exit, sw.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData("completion bash")]
    [InlineData("completion zsh")]
    [InlineData("completion pwsh")]
    public void Parse_SupportedShell_HasNoErrors(string args)
    {
        Assert.Empty(TestCliRoot.Build().Parse(args).Errors);
    }

    [Theory]
    [InlineData("completion fish")]
    [InlineData("completion")]
    public void Parse_UnsupportedOrMissingShell_IsAnError(string args)
    {
        Assert.NotEmpty(TestCliRoot.Build().Parse(args).Errors);
    }

    [Theory]
    [InlineData("bash", "complete -o default -F _umbraco_complete umbraco")]
    [InlineData("zsh", "compdef _umbraco umbraco")]
    [InlineData("pwsh", "Register-ArgumentCompleter -Native -CommandName umbraco")]
    public void ScriptFor_Shell_RegistersACompleterForUmbraco(string shell, string registration)
    {
        Assert.Contains(registration, CompletionCommand.ScriptFor(shell));
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("pwsh")]
    public void ScriptFor_Shell_AsksTheSuggestDirective(string shell)
    {
        // No dotnet-suggest: every script calls the CLI's own directive.
        Assert.Contains("\"[suggest:", CompletionCommand.ScriptFor(shell));
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("pwsh")]
    public void ScriptFor_Shell_HasNoCarriageReturns(string shell)
    {
        // A CR is a syntax error to zsh and to bash on Linux, whatever OS built the tool (#375).
        Assert.DoesNotContain('\r', CompletionCommand.ScriptFor(shell));
    }

    [Theory]
    [InlineData("bash", "\"${cmd/#\\~/$HOME}\" \"[suggest:")]
    [InlineData("zsh", "\"$cmd\" \"[suggest:")]
    [InlineData("pwsh", "& $commandAst.GetCommandName() \"[suggest:")]
    public void ScriptFor_Shell_RunsTheCommandAsTyped(string shell, string invocation)
    {
        // Not whatever `umbraco` is first on PATH (#379).
        Assert.Contains(invocation, CompletionCommand.ScriptFor(shell));
    }

    [Fact]
    public void ScriptFor_Pwsh_FiltersSuggestionsByTheTypedPrefix()
    {
        // PowerShell does not filter a native completer's results itself (#379).
        Assert.Contains(
            "Where-Object { $_.StartsWith($wordToComplete",
            CompletionCommand.ScriptFor("pwsh")
        );
    }

    [Fact]
    public void ScriptFor_UnknownShell_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompletionCommand.ScriptFor("fish"));
    }

    [Fact]
    public async Task Completion_Bash_PrintsTheBareScript()
    {
        var (exit, stdout) = await Run("completion", "bash");

        Assert.Equal((0, CompletionCommand.ScriptFor("bash")), (exit, stdout));
    }

    [Fact]
    public async Task SuggestDirective_PartialNoun_SuggestsTheNoun()
    {
        var (_, stdout) = await Run("[suggest:11]", "umbraco con");

        Assert.Contains("content", stdout.Split(Environment.NewLine));
    }

    [Fact]
    public async Task SuggestDirective_PartialVerb_SuggestsTheVerb()
    {
        var (_, stdout) = await Run("[suggest:19]", "umbraco content cre");

        Assert.Contains("create", stdout.Split(Environment.NewLine));
    }

    [Fact]
    public async Task SuggestDirective_PartialOption_SuggestsTheOption()
    {
        var (_, stdout) = await Run("[suggest:26]", "umbraco data-type list --a");

        Assert.Contains("--all", stdout.Split(Environment.NewLine));
    }

    [Fact]
    public async Task SuggestDirective_OutputOption_SuggestsEveryFormat()
    {
        var (_, stdout) = await Run("[suggest:11]", "umbraco -o ");

        Assert.Subset(
            stdout.Split(Environment.NewLine).ToHashSet(),
            GlobalOptions.OutputFormats.ToHashSet()
        );
    }

    [Fact]
    public async Task SuggestDirective_Options_OmitsTheSlashHelpAliases()
    {
        var (_, stdout) = await Run("[suggest:16]", "umbraco content ");

        Assert.Empty(stdout.Split(Environment.NewLine).Intersect(["/h", "/?"]));
    }

    [Fact]
    public async Task SuggestDirective_Options_KeepsTheDashHelpAliases()
    {
        var (_, stdout) = await Run("[suggest:16]", "umbraco content ");

        Assert.Subset(
            stdout.Split(Environment.NewLine).ToHashSet(),
            new HashSet<string> { "--help", "-h", "-?" }
        );
    }

    [Fact]
    public async Task SuggestDirective_CompletionShell_SuggestsEveryShell()
    {
        var (_, stdout) = await Run("[suggest:19]", "umbraco completion ");

        Assert.Subset(
            stdout.Split(Environment.NewLine).ToHashSet(),
            CompletionCommand.Shells.ToHashSet()
        );
    }
}
