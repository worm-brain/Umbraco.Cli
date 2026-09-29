using BenchmarkDotNet.Attributes;
using Umbraco.Cli.Commands.Content;

namespace Umbraco.Cli.Benchmarks;

/// <summary>
/// The client-free parts of <c>content export</c> / <c>diff</c> / <c>apply</c> on a large site:
/// writing a snapshot file, reading one back, and classifying every document against the live
/// tree. Apply's writes need a server and are out of scope; its plan is this diff.
/// </summary>
[MemoryDiagnoser]
public class ContentPipelineBenchmarks
{
    /// <summary>How many documents the tree holds (see <see cref="Fixtures.ContentSnapshots"/>).</summary>
    private const int Documents = 2000;

    private ContentSnapshot _desired = null!;
    private ContentSnapshot _current = null!;
    private string _desiredJson = null!;

    /// <summary>Builds the two snapshots and the snapshot file text.</summary>
    [GlobalSetup]
    public void Setup()
    {
        (_desired, _current) = Fixtures.ContentSnapshots(Documents);
        _desiredJson = _desired.ToJson();
    }

    /// <summary><c>content diff</c>'s classification: added, changed, removed and publish steps.</summary>
    /// <returns>The diff.</returns>
    [Benchmark]
    public ContentDiff Diff() => ContentDiffEngine.Compare(_desired, _current, "en-US");

    /// <summary><c>content export</c> writing the snapshot file.</summary>
    /// <returns>The snapshot JSON.</returns>
    [Benchmark]
    public string WriteSnapshot() => _desired.ToJson();

    /// <summary><c>content diff</c> / <c>apply</c> reading and validating the snapshot file.</summary>
    /// <returns>The snapshot.</returns>
    [Benchmark]
    public ContentSnapshot ReadSnapshot() => ContentSnapshot.FromJson(_desiredJson);
}
