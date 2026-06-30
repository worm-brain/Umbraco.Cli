using System.Diagnostics;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

/// <summary>
/// The per-invocation collaborators a command needs once it is cleared to run:
/// the resolved output writer, an authenticated client, the command name, and a
/// stopwatch for timing. Built by <see cref="CommandContextFactory"/>.
/// </summary>
public sealed class CommandContext
{
    public required IOutputWriter Output { get; init; }
    public required IUmbracoManagementClient Client { get; init; }
    public required string CommandName { get; init; }
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}
