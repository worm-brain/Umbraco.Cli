using System.ComponentModel;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands.Extensions;

/// <summary>
/// Runs an extension command (ADR 0010): checks the line, applies the allow-list with the
/// extension's noun as its group, and starts the executable with the line's context in its
/// environment. The extension owns stdout from then on; this writes only its own refusals.
/// </summary>
public sealed class ExtensionLauncher
{
    private readonly CommandContextFactory _contexts;
    private readonly Func<
        string,
        IReadOnlyList<string>,
        IReadOnlyDictionary<string, string>,
        CancellationToken,
        Task<int>
    > _run;

    /// <summary>Creates the launcher.</summary>
    /// <param name="contexts">The context factory, whose allow-list and host checks apply.</param>
    /// <param name="run">
    /// Starts the executable and returns its exit code; <see cref="ExtensionProcess.RunAsync"/>
    /// unless a test replaces it.
    /// </param>
    public ExtensionLauncher(
        CommandContextFactory contexts,
        Func<
            string,
            IReadOnlyList<string>,
            IReadOnlyDictionary<string, string>,
            CancellationToken,
            Task<int>
        >? run = null
    )
    {
        _contexts = contexts;
        _run = run ?? ExtensionProcess.RunAsync;
    }

    /// <summary>Runs <paramref name="invocation"/> with <paramref name="executable"/>.</summary>
    /// <param name="invocation">The extension command line.</param>
    /// <param name="executable">The executable <see cref="ExtensionLocator.Find"/> resolved for its noun.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The extension's exit code; <c>1</c> for a malformed context option or an executable that
    /// would not start; <c>2</c> when the allow-list or the credential-host rule refused it.
    /// </returns>
    public async Task<int> RunAsync(
        ExtensionInvocation invocation,
        string executable,
        CancellationToken ct = default
    )
    {
        var noun = invocation.Noun;
        var context = invocation.Context;
        var output = OutputWriterFactory.Create(
            OutputFormatParser.Parse(
                context.Output ?? Environment.GetEnvironmentVariable(GlobalOptions.OutputVariable)
            )
        );

        // A malformed option is the same mistake on any command line: invalid_argument, exit 1,
        // as the parse-error reporter says it for a built-in one.
        if (invocation.Problem is { } problem)
        {
            output.WriteError(ExitCode.Failed, FailureCategory.InvalidArgument, problem, noun);
            return (int)ExitCode.Failed;
        }

        if (
            _contexts.RefuseExtension(
                noun,
                context.Config,
                context.Profile,
                context.Host,
                context.Token,
                output
            ) is
            { } refused
        )
            return refused;

        try
        {
            return await _run(executable, invocation.Arguments, context.ChildEnvironment(), ct);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Found but not runnable (deleted since, no permission, not a real executable).
            output.WriteError(
                ExitCode.Failed,
                FailureCategory.Internal,
                $"Could not run {executable}: {ex.Message}",
                noun
            );
            return (int)ExitCode.Failed;
        }
    }
}
