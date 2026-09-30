using System.CommandLine;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The environment variables every command reads as a global option's default (ADR 0010), which
/// is how an extension command's calls back into the CLI inherit the line that launched them. An
/// option on the command line always wins over its variable.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class GlobalOptionsEnvironmentTests : IDisposable
{
    private static readonly string[] Variables =
    [
        GlobalOptions.ConfigVariable,
        GlobalOptions.TokenVariable,
        GlobalOptions.OutputVariable,
        GlobalOptions.DryRunVariable,
    ];

    public GlobalOptionsEnvironmentTests() => Clear();

    public void Dispose() => Clear();

    private static void Clear()
    {
        foreach (var name in Variables)
            Environment.SetEnvironmentVariable(name, null);
    }

    private static (GlobalOptions Global, ParseResult Parsed) Parse(string args)
    {
        var global = new GlobalOptions();
        var root = new RootCommand();
        global.AddTo(root);
        return (global, root.Parse(args));
    }

    [Fact]
    public void FormatOf_VariableAndNoOption_IsTheVariablesFormat()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.OutputVariable, "CSV");
        var (global, parsed) = Parse("");

        Assert.Equal(OutputFormat.Csv, global.FormatOf(parsed));
    }

    [Fact]
    public void FormatOf_OptionAndVariable_TheOptionWins()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.OutputVariable, "csv");
        var (global, parsed) = Parse("--output human");

        Assert.Equal(OutputFormat.Human, global.FormatOf(parsed));
    }

    [Fact]
    public void FormatOf_VariableThatIsNotAFormat_IsIgnored()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.OutputVariable, "yaml");
        var (global, parsed) = Parse("");

        Assert.Null(global.FormatOf(parsed));
    }

    [Fact]
    public void ConfigPath_VariableAndNoOption_IsTheVariablesPath()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.ConfigVariable, "/etc/umbraco/ci.json");
        var (global, parsed) = Parse("");

        Assert.Equal("/etc/umbraco/ci.json", global.ConfigPath(parsed));
    }

    [Fact]
    public void ConfigPath_OptionAndVariable_TheOptionWins()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.ConfigVariable, "/etc/umbraco/ci.json");
        var (global, parsed) = Parse("--config mine.json");

        Assert.Equal("mine.json", global.ConfigPath(parsed));
    }

    [Fact]
    public void TokenOf_VariableAndNoOption_IsTheVariablesToken()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.TokenVariable, "t0ken");
        var (global, parsed) = Parse("");

        Assert.Equal("t0ken", global.TokenOf(parsed));
    }

    [Fact]
    public void TokenOf_BlankVariable_IsNoToken()
    {
        Environment.SetEnvironmentVariable(GlobalOptions.TokenVariable, " ");
        var (global, parsed) = Parse("");

        Assert.Null(global.TokenOf(parsed));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    public void IsDryRun_Variable_TurnsItOnWhenTruthy(string value, bool expected)
    {
        Environment.SetEnvironmentVariable(GlobalOptions.DryRunVariable, value);
        var (global, parsed) = Parse("");

        Assert.Equal(expected, global.IsDryRun(parsed));
    }
}
