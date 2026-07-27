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

    /// <summary>
    /// Whether <c>--yes</c> was supplied, bypassing the confirmation prompt on destructive
    /// commands. Also implicitly required to run a destructive command non-interactively.
    /// </summary>
    public bool AssumeYes { get; init; }

    /// <summary>
    /// Whether <c>--dry-run</c> is active. A dry run never sends the mutation (it is aborted
    /// at the HTTP layer and previewed), so the destructive-op confirmation gate is skipped —
    /// previewing a delete is harmless and must not force <c>--yes</c>.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Whether read-only mode is active. A write will be refused at the HTTP layer, so the
    /// destructive-op confirmation gate is skipped — there is no point prompting to confirm a
    /// delete that read-only mode will then reject.
    /// </summary>
    public bool ReadOnly { get; init; }

    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}
