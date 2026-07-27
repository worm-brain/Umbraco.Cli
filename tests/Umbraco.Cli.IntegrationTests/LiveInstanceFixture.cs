namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// Shared fixture that probes once for a reachable, authenticated Umbraco instance by
/// running <c>auth whoami</c>. Tests use <see cref="IsReachable"/> with <c>Skip.IfNot</c>
/// so the suite runs against a developer/CI instance when present and is skipped (not
/// failed) otherwise - keeping solution-wide <c>dotnet test</c> green without an instance.
/// </summary>
public sealed class LiveInstanceFixture
{
    /// <summary>Whether an authenticated instance responded to <c>auth whoami</c>.</summary>
    public bool IsReachable { get; }

    /// <summary>Reason shown on skipped tests when no instance is reachable.</summary>
    public string SkipReason =>
        "No reachable Umbraco instance. Set UMBRACO_HOST/UMBRACO_CLIENT_ID/"
        + "UMBRACO_CLIENT_SECRET (or run `umbraco auth login`) to enable integration tests.";

    /// <summary>Runs the one-time reachability probe.</summary>
    public LiveInstanceFixture()
    {
        try
        {
            IsReachable = CliRunner.Run("auth", "whoami").Ok;
        }
        catch
        {
            // Any failure to even launch the CLI means we cannot run integration tests.
            IsReachable = false;
        }
    }
}

/// <summary>
/// xUnit collection binding the <see cref="LiveInstanceFixture"/> so the reachability probe
/// runs once and is shared across all integration tests.
/// </summary>
[CollectionDefinition("Live")]
public sealed class LiveCollection : ICollectionFixture<LiveInstanceFixture>;
