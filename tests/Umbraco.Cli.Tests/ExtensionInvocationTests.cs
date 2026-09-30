using System.CommandLine;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Extensions;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Which command lines name an extension command, and how one is split between the CLI and the
/// extension (ADR 0010): the context options are the CLI's wherever they appear, everything else
/// is the extension's, in order.
/// </summary>
public class ExtensionInvocationTests
{
    private static readonly RootCommand Root = TestCliRoot.Build();

    private static ExtensionInvocation? From(params string[] args) =>
        ExtensionInvocation.From(args, Root, new GlobalOptions());

    [Fact]
    public void From_UnknownNoun_PassesTheRestInOrder()
    {
        var invocation = From("foo", "bar", "--x", "1");

        Assert.Equal("foo", invocation?.Noun);
        Assert.Equal(["bar", "--x", "1"], invocation?.Arguments);
    }

    [Theory]
    [InlineData("content", "list")]
    [InlineData("api", "get", "/umbraco/management/api/v1/server/status")]
    [InlineData("commands")]
    public void From_BuiltInNoun_IsNotAnExtension(params string[] args)
    {
        Assert.Null(From(args));
    }

    [Theory]
    [InlineData]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("-o", "json")]
    [InlineData("--", "foo")]
    public void From_LineWithNoNoun_IsNotAnExtension(params string[] args)
    {
        Assert.Null(From(args));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("./foo")]
    [InlineData("foo.exe")]
    public void From_WordThatIsNotANoun_IsNotAnExtension(string word)
    {
        Assert.Null(From(word, "bar"));
    }

    [Fact]
    public void From_ContextOptionsAnywhere_AreTakenOutIntoTheContext()
    {
        var invocation = From(
            "--profile",
            "staging",
            "foo",
            "export",
            "--dry-run",
            "-o",
            "human",
            "--readonly"
        );

        Assert.Equal(
            new ExtensionContext(Profile: "staging", Output: "human", ReadOnly: true, DryRun: true),
            invocation?.Context
        );
        Assert.Equal(["export"], invocation?.Arguments);
    }

    [Fact]
    public void From_OtherGlobalOptions_AreTheExtensionsArguments()
    {
        var invocation = From("foo", "purge", "--yes", "-q", "--fields", "id,name", "-v");

        Assert.Equal(["purge", "--yes", "-q", "--fields", "id,name", "-v"], invocation?.Arguments);
    }

    [Fact]
    public void From_OtherGlobalOptionBeforeTheNoun_IsPassedOnWithItsValue()
    {
        var invocation = From("--fields", "id", "foo", "list");

        Assert.Equal("foo", invocation?.Noun);
        Assert.Equal(["--fields", "id", "list"], invocation?.Arguments);
    }

    [Fact]
    public void From_DoubleDash_PassesEverythingAfterItAsItIs()
    {
        var invocation = From("foo", "run", "--", "--dry-run");

        Assert.Equal(new ExtensionContext(), invocation?.Context);
        Assert.Equal(["run", "--", "--dry-run"], invocation?.Arguments);
    }

    [Theory]
    [InlineData("--host=https://other.example")]
    [InlineData("--host:https://other.example")]
    public void From_AttachedValue_IsTheOptionsValue(string token)
    {
        var invocation = From(token, "foo");

        Assert.Equal("https://other.example", invocation?.Context.Host);
    }

    [Fact]
    public void From_ContextOptionWithoutAValue_IsAProblem()
    {
        var invocation = From("foo", "export", "--profile");

        Assert.Equal("--profile needs a value.", invocation?.Problem);
    }

    [Fact]
    public void From_UnknownOutputFormat_IsAProblem()
    {
        var invocation = From("foo", "-o", "yaml");

        Assert.Equal(
            "'yaml' is not a valid --output format: expected one of json, human, csv.",
            invocation?.Problem
        );
    }

    [Fact]
    public void From_FlagSetFalse_IsOff()
    {
        var invocation = From("foo", "--dry-run=false");

        Assert.False(invocation?.Context.DryRun);
    }

    [Fact]
    public void From_FlagWithAValueThatIsNotABoolean_IsAProblemAndStaysOn()
    {
        var invocation = From("foo", "--readonly=maybe");

        Assert.Equal(
            "'maybe' is not valid for --readonly: expected true or false.",
            invocation?.Problem
        );
        Assert.True(invocation?.Context.ReadOnly);
    }

    [Fact]
    public void ChildEnvironment_NothingGiven_SetsNothing()
    {
        Assert.Empty(new ExtensionContext().ChildEnvironment());
    }

    [Fact]
    public void ChildEnvironment_EveryContextOption_BecomesTheVariableTheCliReadsForIt()
    {
        var context = new ExtensionContext(
            Host: "https://site.example",
            Token: "t0ken",
            Config: "ci.json",
            Profile: "staging",
            Output: "HUMAN",
            ReadOnly: true,
            DryRun: true
        );

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["UMBRACO_HOST"] = "https://site.example",
                ["UMBRACO_TOKEN"] = "t0ken",
                // Absolute, so the extension can run its calls from any directory.
                ["UMBRACO_CONFIG"] = Path.GetFullPath("ci.json"),
                ["UMBRACO_PROFILE"] = "staging",
                ["UMBRACO_OUTPUT"] = "human",
                ["UMBRACO_READONLY"] = "1",
                ["UMBRACO_DRY_RUN"] = "1",
            },
            context.ChildEnvironment()
        );
    }

    [Fact]
    public void ChildEnvironment_NoTokenGiven_PassesNoToken()
    {
        // ADR 0010: a token leaves the CLI only when the caller gave it with --token.
        var env = new ExtensionContext(Host: "https://site.example").ChildEnvironment();

        Assert.DoesNotContain("UMBRACO_TOKEN", env.Keys);
    }
}
