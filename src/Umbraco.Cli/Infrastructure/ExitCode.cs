namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The process exit codes (docs/conventions.md, section 7) - the only place they are defined.
/// A caller gates on the code first, then reads the error envelope's <c>category</c> for detail.
/// </summary>
public enum ExitCode
{
    /// <summary>Success, including a <c>--dry-run</c> preview.</summary>
    Success = 0,

    /// <summary>The command ran and failed: an API error, invalid input, a partly failed bulk run, or an unexpected error.</summary>
    Failed = 1,

    /// <summary>Aborted before running: not authenticated, blocked by policy, confirmation missing, or refused by a pre-flight check.</summary>
    Aborted = 2,

    /// <summary>Cancelled with Ctrl-C (128 + SIGINT).</summary>
    Cancelled = 130,
}
