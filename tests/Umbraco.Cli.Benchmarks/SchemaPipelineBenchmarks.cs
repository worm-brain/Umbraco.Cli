using BenchmarkDotNet.Attributes;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Benchmarks;

/// <summary>
/// The client-free parts of <c>schema export</c> / <c>diff</c> / <c>apply</c> on a mid-sized
/// site: writing a snapshot file, reading one back, and matching every entity against the live
/// schema. Apply's writes need a server and are out of scope; its plan is this diff.
/// </summary>
[MemoryDiagnoser]
public class SchemaPipelineBenchmarks
{
    /// <summary>How many document types the schema holds (see <see cref="Fixtures.SchemaSnapshots"/>).</summary>
    private const int DocumentTypes = 150;

    private SchemaSnapshot _desired = null!;
    private SchemaSnapshot _current = null!;
    private string _desiredJson = null!;

    /// <summary>Builds the two snapshots and the snapshot file text.</summary>
    [GlobalSetup]
    public void Setup()
    {
        (_desired, _current) = Fixtures.SchemaSnapshots(DocumentTypes);
        _desiredJson = _desired.ToJson();
    }

    /// <summary>
    /// <c>schema diff</c>'s matching: by id then key, with reference rewriting, container matching
    /// and a path-level comparison of every matched pair.
    /// </summary>
    /// <returns>The diff.</returns>
    [Benchmark]
    public SchemaDiff Diff() => SchemaDiffEngine.Compare(_desired, _current);

    /// <summary><c>schema export</c> writing the snapshot file.</summary>
    /// <returns>The snapshot JSON.</returns>
    [Benchmark]
    public string WriteSnapshot() => _desired.ToJson();

    /// <summary><c>schema diff</c> / <c>apply</c> reading and validating the snapshot file.</summary>
    /// <returns>The snapshot.</returns>
    [Benchmark]
    public SchemaSnapshot ReadSnapshot() => SchemaSnapshot.FromJson(_desiredJson);
}
