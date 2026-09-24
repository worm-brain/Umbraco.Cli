namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// End-to-end schema pipeline tests (#68) against a live instance. These are deliberately
/// non-mutating: they export the live schema, then assert the round-trip property that a fresh
/// export diffs/applies as a no-op against the instance it came from. That exercises the whole
/// export -> diff -> apply plan path (including raw-fidelity reads and GUID matching) without
/// creating or deleting real schema on a possibly-shared instance. Skipped when no instance is
/// reachable (see <see cref="LiveInstanceFixture"/>).
/// </summary>
/// <param name="live">Shared reachability fixture.</param>
[Collection("Live")]
public sealed class SchemaIntegrationTests(LiveInstanceFixture live) : LiveTestBase(live)
{
    [SkippableFact]
    public void Export_WritesSnapshotFile()
    {
        RequireLive();
        var path = Path.Combine(Path.GetTempPath(), $"schema-int-{Guid.NewGuid()}.json");
        try
        {
            var result = CliRunner.Run("schema", "export", "--out", path);

            Assert.True(result.Ok, result.Stderr);
            Assert.True(File.Exists(path));
            // The summary reports the per-kind counts that were written. Media types and member
            // types are asserted positive, not >= 0: every Umbraco install ships with some, so a
            // zero here means the new enumeration (#186) found nothing, not that the site is bare.
            Assert.True(result.Data().GetProperty("documentTypes").GetInt32() >= 0);
            Assert.True(result.Data().GetProperty("mediaTypes").GetInt32() > 0);
            Assert.True(result.Data().GetProperty("memberTypes").GetInt32() > 0);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [SkippableFact]
    public void ExportThenDiff_AgainstSameInstance_ShowsNoDrift()
    {
        RequireLive();
        var path = Path.Combine(Path.GetTempPath(), $"schema-int-{Guid.NewGuid()}.json");
        try
        {
            Assert.True(CliRunner.Run("schema", "export", "--out", path).Ok);

            // Round-trip property: a snapshot diffed against the instance it came from must show
            // zero actionable changes (empty rows array). This guards the raw-fidelity read +
            // GUID-primary matching + order-insensitive comparison all agreeing with each other.
            var diff = CliRunner.Run("schema", "diff", path);

            Assert.True(diff.Ok, diff.Stderr);
            Assert.Equal(JsonValueKind.Array, diff.Data().ValueKind);
            Assert.Equal(0, diff.Data().GetArrayLength());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [SkippableFact]
    public void ExportThenApplyDryRun_AgainstSameInstance_PlansNothing()
    {
        RequireLive();
        var path = Path.Combine(Path.GetTempPath(), $"schema-int-{Guid.NewGuid()}.json");
        try
        {
            Assert.True(CliRunner.Run("schema", "export", "--out", path).Ok);

            // A dry-run apply of an in-sync snapshot must produce an empty plan and write nothing.
            var apply = CliRunner.Run("schema", "apply", path, "--dry-run");

            Assert.True(apply.Ok, apply.Stderr);
            Assert.Equal(0, apply.Data().GetArrayLength());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
