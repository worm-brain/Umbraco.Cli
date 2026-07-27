using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Http;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Runs the pipeline shared by every API-backed command: build the context (auth,
/// host, output), invoke a single client call, map a failed <see cref="UmbracoResponse{T}"/>
/// to an error + exit code, and render the result on success. Each command supplies
/// only what differs — its name, its client call, and how to render the result.
/// </summary>
public sealed class CommandExecutor
{
    private readonly CommandContextFactory _factory;
    private readonly IConfirmationPrompt _confirmation;

    public CommandExecutor(CommandContextFactory factory, IConfirmationPrompt confirmation)
    {
        _factory = factory;
        _confirmation = confirmation;
    }

    /// <summary>
    /// Exit codes: <c>0</c> success · <c>1</c> API failure · <c>2</c> aborted before
    /// running (no host / not authenticated, or a destructive command refused/declined
    /// without <c>--yes</c>) · <c>130</c> cancelled (Ctrl-C).
    /// </summary>
    public async Task<int> RunAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Action<CommandContext, T?> render,
        CancellationToken ct,
        string? confirmationPrompt = null
    )
    {
        CommandContext ctx;
        try
        {
            ctx = await _factory.CreateAsync(parseResult, commandName, ct);
        }
        catch (CommandAbortedException)
        {
            return 2;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130;
        }

        // Destructive-op gate (#70): a command that supplies a confirmation prompt must be
        // confirmed before it runs, unless --yes was given. Non-interactively (piped/scripted/
        // agent) we never prompt — we abort and require --yes, so a destructive op can never
        // happen silently. Skipped under --dry-run: the mutation is never sent (it is aborted
        // and previewed at the HTTP layer), so there is nothing to confirm.
        if (confirmationPrompt is not null && !ctx.AssumeYes && !ctx.DryRun)
        {
            if (!_confirmation.IsInteractive)
            {
                ctx.Output.WriteError(
                    2,
                    $"{confirmationPrompt} Refusing to run a destructive operation without "
                        + "confirmation. Re-run with --yes to proceed (required in non-interactive mode)."
                );
                return 2;
            }

            if (!_confirmation.Confirm(confirmationPrompt))
            {
                ctx.Output.WriteError(2, "Operation cancelled.");
                return 2;
            }
        }

        try
        {
            var result = await call(ctx.Client, ct);
            if (!result.IsSuccess)
            {
                // Permission-aware failure (#70): a raw 403 is opaque, so translate it into an
                // actionable message while preserving any detail the API returned.
                var message =
                    result.StatusCode == 403
                        ? "Your API user is not permitted to perform this operation (403). "
                            + "Check its user-group permissions in Umbraco."
                            + (
                                string.IsNullOrWhiteSpace(result.ErrorMessage)
                                    ? ""
                                    : $" ({result.ErrorMessage})"
                            )
                        : result.ErrorMessage!;
                ctx.Output.WriteError(result.StatusCode, message);
                return 1;
            }

            render(ctx, result.Data);
            return 0;
        }
        catch (DryRunException dry)
        {
            // --dry-run: the mutation-interceptor aborted a write before it was sent. Print
            // the captured request instead of executing it, and report success (exit 0) —
            // nothing was changed.
            ctx.Output.WriteDryRun(dry.Method, dry.Url, dry.Body);
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130; // 128 + SIGINT — distinct from a config abort (2)
        }
        catch (Exception ex)
        {
            // Backstop: any unforeseen failure (e.g. a malformed --json-body, a missing
            // upload file) becomes a clean error instead of a raw stack trace.
            ctx.Output.WriteError(1, ex.Message);
            return 1;
        }
    }

    /// <summary>Renders the result object via <see cref="IOutputWriter.WriteSuccess"/>.</summary>
    public Task<int> RunObjectAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        CancellationToken ct
    ) =>
        RunAsync(
            parseResult,
            commandName,
            call,
            static (ctx, data) =>
                ctx.Output.WriteSuccess(data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds),
            ct
        );

    /// <summary>
    /// Writes a fixed success message via <see cref="IOutputWriter.WriteMessage"/>. Supply
    /// <paramref name="confirmationPrompt"/> for a destructive command (delete): the user is
    /// asked to confirm before it runs unless <c>--yes</c> is given (#70).
    /// </summary>
    public Task<int> RunMessageAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        string successMessage,
        CancellationToken ct,
        string? confirmationPrompt = null
    ) =>
        RunAsync(
            parseResult,
            commandName,
            call,
            (ctx, _) => ctx.Output.WriteMessage(successMessage),
            ct,
            confirmationPrompt
        );

    /// <summary>Projects the result into table rows via <see cref="IOutputWriter.WriteTable"/>.</summary>
    public Task<int> RunTableAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        string[] headers,
        Func<T?, IEnumerable<string[]>> rows,
        CancellationToken ct
    ) =>
        RunAsync(
            parseResult,
            commandName,
            call,
            (ctx, data) => ctx.Output.WriteTable(headers, rows(data)),
            ct
        );
}
