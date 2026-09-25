using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// One spelling of "on" for every on/off environment variable (<c>UMBRACO_READONLY</c>,
/// <c>UMBRACO_NO_TOKEN_CACHE</c>).
/// </summary>
public class EnvironmentFlagsTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("YES")]
    [InlineData(" 1 ")] // a stray space from `set VAR=1 ` must not turn a guardrail off
    public void IsTruthy_OnValues_AreOn(string value) =>
        Assert.True(EnvironmentFlags.IsTruthy(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("on")]
    public void IsTruthy_AnythingElse_IsOff(string? value) =>
        Assert.False(EnvironmentFlags.IsTruthy(value));
}
