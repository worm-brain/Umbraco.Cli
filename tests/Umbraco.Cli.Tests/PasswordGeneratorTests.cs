using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Tests for <see cref="PasswordGenerator"/>, which produces the fallback password for
/// <c>members create</c> when none is supplied (#136). The generated value must satisfy
/// Umbraco's default complexity policy or the create is rejected.
/// </summary>
public class PasswordGeneratorTests
{
    /// <summary>Happy path: the generated password meets the default complexity policy.</summary>
    [Fact]
    public void Generate_MeetsDefaultComplexityPolicy()
    {
        var pw = PasswordGenerator.Generate();

        Assert.True(pw.Length >= 12, $"length was {pw.Length}");
        Assert.Contains(pw, c => char.IsLower(c));
        Assert.Contains(pw, c => char.IsUpper(c));
        Assert.Contains(pw, c => char.IsDigit(c));
        Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
    }

    /// <summary>A requested length below the floor is raised to the minimum, still compliant.</summary>
    [Fact]
    public void Generate_ShortLengthRequested_RaisedToMinimumAndStillCompliant()
    {
        var pw = PasswordGenerator.Generate(4);

        Assert.True(pw.Length >= 12);
        Assert.Contains(pw, c => char.IsUpper(c));
        Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
    }

    /// <summary>Two generations differ (the RNG is actually used).</summary>
    [Fact]
    public void Generate_ProducesDifferentValues()
    {
        Assert.NotEqual(PasswordGenerator.Generate(), PasswordGenerator.Generate());
    }
}
