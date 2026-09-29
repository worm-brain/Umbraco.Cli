using System.CommandLine;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Benchmarks;

/// <summary>
/// The work every <c>umbraco</c> invocation does before its command runs: build the container,
/// build the command tree from it, and parse the arguments. All three go through
/// <see cref="CliServices"/>, the composition root <c>Program.cs</c> uses, so this measures the
/// shipped wiring rather than a copy of it. Process start and JIT are not included (a warmed-up
/// benchmark cannot see them); the hyperfine harness measures those end to end.
/// </summary>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    private ServiceProvider _services = null!;
    private RootCommand _root = null!;

    /// <summary>Builds the tree <see cref="Parse"/> reuses, as one process would.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _services = CliServices.CreateProvider();
        _root = CliServices.BuildRoot(_services);
    }

    /// <summary>Disposes the container built in <see cref="Setup"/>.</summary>
    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();

    /// <summary>
    /// Registers every service and resolves the executor, which pulls in the context factory,
    /// the auth service and the HTTP client factory: the container's share of startup.
    /// </summary>
    /// <returns>The executor, so the resolve is not optimised away.</returns>
    [Benchmark]
    public CommandExecutor BuildContainer()
    {
        using var services = CliServices.CreateProvider();
        return services.GetRequiredService<CommandExecutor>();
    }

    /// <summary>
    /// Builds a fresh container and the whole command tree from it, as <c>Program.cs</c> does
    /// on every run. Each call starts from a new container because the tree's global options are
    /// container singletons: reusing one would add them to a new root every call.
    /// </summary>
    /// <returns>The root command.</returns>
    [Benchmark]
    public RootCommand BuildContainerAndCommandTree()
    {
        using var services = CliServices.CreateProvider();
        return CliServices.BuildRoot(services);
    }

    /// <summary>Parses a typical list invocation against the already-built tree.</summary>
    /// <returns>The parse result.</returns>
    [Benchmark]
    public ParseResult Parse() =>
        _root.Parse(
            [
                "content",
                "list",
                "--parent",
                "5d2f7a3c-1e2b-4c5d-8e9f-0a1b2c3d4e5f",
                "--take",
                "50",
                "--output",
                "json",
            ],
            CliParserConfiguration.Create()
        );
}
