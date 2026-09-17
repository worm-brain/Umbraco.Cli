using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;
using Umbraco.Cli.Infrastructure.Http;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Commands;

/// <summary>
/// One item's outcome in a bulk operation (#85): the id as supplied, a status
/// (<c>success</c> / <c>error</c> / <c>dry-run</c>), and an error message when it failed.
/// Serialized into the results array so a script can see exactly what happened to each id.
/// </summary>
/// <param name="Id">The id as it appeared in the input (so a malformed line is echoed back).</param>
/// <param name="Status">The per-item outcome: <c>success</c>, <c>error</c>, or <c>dry-run</c>.</param>
/// <param name="Error">The failure message when <see cref="Status"/> is <c>error</c>; otherwise null.</param>
public sealed record BulkItemResult(string Id, string Status, string? Error);

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
    /// Exit codes: <c>0</c> success · <c>1</c> API failure · <c>2</c> aborted (no host / not
    /// authenticated · a command blocked by the allow-list · a destructive command
    /// refused/declined without <c>--yes</c> · a write blocked by <c>--readonly</c>) ·
    /// <c>130</c> cancelled (Ctrl-C). Note a <c>--readonly</c> block can fire after prerequisite
    /// reads (e.g. alias→id resolution) have already run.
    /// </summary>
    public Task<int> RunAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Action<CommandContext, T?> render,
        CancellationToken ct,
        string? confirmationPrompt = null
    ) =>
        // The vast majority of commands are a single client call; expose the client-only shape
        // and delegate to the context-aware core below.
        RunContextualAsync(
            parseResult,
            commandName,
            (ctx, c) => call(ctx.Client, c),
            render,
            ct,
            confirmationPrompt
        );

    /// <summary>
    /// Context-aware variant of <see cref="RunAsync{T}"/>: the operation receives the whole
    /// <see cref="CommandContext"/> (not just the client), so a command that must branch on
    /// <see cref="CommandContext.DryRun"/> / <see cref="CommandContext.ReadOnly"/> before doing
    /// its work can. Used by <c>schema apply</c>, whose <c>--dry-run</c> previews a *multi-write*
    /// plan and so cannot rely on the per-request mutation interceptor (which aborts on the first
    /// write). Shares the identical context-build, confirmation gate, error mapping, and
    /// exit-code handling.
    /// </summary>
    /// <typeparam name="T">The rendered payload type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The operation, receiving the built context.</param>
    /// <param name="render">Renders the payload on success.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="confirmationPrompt">A destructive-op prompt, or null.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunContextualAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<CommandContext, CancellationToken, Task<UmbracoResponse<T>>> call,
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
        // happen silently. Skipped under --dry-run (the mutation is previewed, not sent) and
        // under --readonly (the write will be refused at the HTTP layer), so we never prompt to
        // confirm an operation that isn't going to execute anyway.
        if (confirmationPrompt is not null && !ctx.AssumeYes && !ctx.DryRun && !ctx.ReadOnly)
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
            var result = await call(ctx, ct);
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
        catch (ReadOnlyModeException ro)
        {
            // --readonly: the mutation-interceptor refused a write. Report a clear error and a
            // non-zero exit; nothing was changed.
            ctx.Output.WriteError(
                2,
                $"Read-only mode is active ({ro.Method} {ro.Url} was blocked). This command "
                    + "performs a write, which is not allowed under --readonly / UMBRACO_READONLY."
            );
            return 2;
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

    /// <summary>
    /// Runs a single operation over many ids (#85), collecting a per-item result so a script can
    /// pipe ids in and see exactly what happened to each. Shares the run pipeline with
    /// <see cref="RunAsync{T}"/> — context build, allow-list/readonly/dry-run policy — but
    /// applies the destructive-op confirmation gate <em>once</em> for the whole batch (a bulk
    /// delete must not prompt per item, and non-interactively still requires <c>--yes</c>, #70).
    /// Per-item failures are captured, not fatal; the batch runs to completion. Exit code is
    /// <c>0</c> when every item succeeded, <c>1</c> when any item failed.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name (e.g. <c>content.bulk.delete</c>).</param>
    /// <param name="readIds">
    /// Reads the raw id lines from stdin/file. Invoked after the context is built so an IO error
    /// (e.g. a missing file) is reported through the resolved output writer.
    /// </param>
    /// <param name="callPerId">The client call to run for each parsed id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="confirmationPrompt">A batch confirmation prompt for a destructive bulk op, or null.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunBulkAsync(
        ParseResult parseResult,
        string commandName,
        Func<IReadOnlyList<string>> readIds,
        Func<
            IUmbracoManagementClient,
            Guid,
            CancellationToken,
            Task<UmbracoResponse<Empty>>
        > callPerId,
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

        // Read-only mode refuses writes, so a bulk write is blocked before it runs — mirror the
        // single-op contract (exit 2 with a clear error) rather than running the loop and
        // reporting every item as a failure (which would read as an API error, exit 1). Bulk
        // operations are all writes, so this applies to the whole group.
        if (ctx.ReadOnly)
        {
            ctx.Output.WriteError(
                2,
                "Read-only mode is active (--readonly / UMBRACO_READONLY). This bulk command "
                    + "performs writes, which are not allowed."
            );
            return 2;
        }

        // Read the ids now that the output writer exists, so a missing/unreadable file becomes a
        // clean error rather than an unhandled exception.
        IReadOnlyList<string> rawIds;
        try
        {
            rawIds = readIds();
        }
        catch (Exception ex)
        {
            ctx.Output.WriteError(2, $"Could not read ids: {ex.Message}");
            return 2;
        }

        // Nothing to do — surface an empty result rather than silently exiting.
        if (rawIds.Count == 0)
        {
            ctx.Output.WriteError(2, "No ids supplied. Provide ids via --file <path> or stdin.");
            return 2;
        }

        // Destructive-op gate (#70), applied ONCE for the whole batch. Skipped under --dry-run
        // (previewed, not sent) and --readonly (refused at the HTTP layer) exactly as the
        // single-op path does.
        if (confirmationPrompt is not null && !ctx.AssumeYes && !ctx.DryRun && !ctx.ReadOnly)
        {
            if (!_confirmation.IsInteractive)
            {
                ctx.Output.WriteError(
                    2,
                    $"{confirmationPrompt} Refusing to run a destructive bulk operation without "
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

        var results = new List<BulkItemResult>(rawIds.Count);
        var anyFailed = false;
        foreach (var raw in rawIds)
        {
            if (!Guid.TryParse(raw, out var id))
            {
                results.Add(new BulkItemResult(raw, "error", "Not a valid GUID."));
                anyFailed = true;
                continue;
            }

            try
            {
                var result = await callPerId(ctx.Client, id, ct);
                if (result.IsSuccess)
                {
                    results.Add(new BulkItemResult(raw, "success", null));
                }
                else
                {
                    results.Add(new BulkItemResult(raw, "error", result.ErrorMessage));
                    anyFailed = true;
                }
            }
            catch (DryRunException)
            {
                // Under --dry-run each write is aborted before it is sent; record the preview
                // intent per id rather than printing N request envelopes.
                results.Add(new BulkItemResult(raw, "dry-run", null));
            }
            catch (ReadOnlyModeException)
            {
                results.Add(new BulkItemResult(raw, "error", "Blocked by --readonly."));
                anyFailed = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return 130; // Ctrl-C mid-batch
            }
            catch (Exception ex)
            {
                results.Add(new BulkItemResult(raw, "error", ex.Message));
                anyFailed = true;
            }
        }

        ctx.Output.WriteSuccess(results, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
        return anyFailed ? 1 : 0;
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
            (ctx, _) =>
                ctx.Output.WriteMessage(
                    successMessage,
                    ctx.CommandName,
                    ctx.Stopwatch.ElapsedMilliseconds
                ),
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
            (ctx, data) =>
                ctx.Output.WriteTable(
                    headers,
                    rows(data),
                    ctx.CommandName,
                    ctx.Stopwatch.ElapsedMilliseconds
                ),
            ct
        );
}
