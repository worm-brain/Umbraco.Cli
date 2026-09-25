using System.CommandLine;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Cross-option validators read options through <see cref="CommandValidation.TryGetValue{T}(System.CommandLine.Parsing.CommandResult, Option{T}, out T)"/>,
/// so a malformed value leaves only its own parse error rather than crashing the rule.
/// </summary>
public class CommandValidationTests
{
    /// <summary>Parses <paramref name="args"/> and returns what the validator saw.</summary>
    private static (bool Parsed, Guid[]? Value) SeenByValidator(string args)
    {
        var ids = ListOption.Guids("--ids", "Ids.");
        var root = new RootCommand { ids };
        (bool, Guid[]?) seen = default;
        root.Validators.Add(result =>
        {
            var parsed = result.TryGetValue(ids, out var value);
            seen = (parsed, value);
        });
        root.Parse(args);
        return seen;
    }

    [Fact]
    public void TryGetValue_AValueThatParses_IsRead()
    {
        var id = Guid.NewGuid();

        Assert.Equal(
            (true, id),
            (SeenByValidator($"--ids {id}").Parsed, SeenByValidator($"--ids {id}").Value!.Single())
        );
    }

    [Fact]
    public void TryGetValue_AValueThatDoesNotParse_ReportsFalseInsteadOfThrowing()
    {
        Assert.False(SeenByValidator("--ids nope").Parsed);
    }
}
