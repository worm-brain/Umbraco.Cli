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
        CancellationToken ct
    ) =>
        // The vast majority of commands are a single client call; expose the client-only shape
        // and delegate to the context-aware core below.
        RunContextualAsync(parseResult, commandName, (ctx, c) => call(ctx.Client, c), render, ct);

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
    /// <returns>The process exit code.</returns>
    public async Task<int> RunContextualAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<CommandContext, CancellationToken, Task<UmbracoResponse<T>>> call,
        Action<CommandContext, T?> render,
        CancellationToken ct
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

        // Destructive-op gate (#70): a command declared destructive (CommandSafety, #255) must be
        // confirmed before it runs, unless --yes was given. Non-interactively (piped/scripted/
        // agent) we never prompt — we abort and require --yes, so a destructive op can never
        // happen silently. Skipped under --dry-run (the mutation is previewed, not sent) and
        // under --readonly (the write will be refused at the HTTP layer), so we never prompt to
        // confirm an operation that isn't going to execute anyway.
        var confirmationPrompt = CommandSafety.PromptFor(parseResult);
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
                ctx.Output.WriteError(2, "Operation cancelled.", commandName: ctx.CommandName);
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

                // Attribution context (#152): tag the error with its failure category and, when
                // the server actually responded, the server version - so a caller can tell a
                // server-side fault from a rejected request. The version is only fetched when the
                // server responded (a category that saw an HTTP status); for unreachable/timeout
                // the lookup would just fail or hang again, so it is skipped. Best-effort: a null
                // version is simply omitted and never delays or masks this error.
                var serverVersion = result.Category
                    is FailureCategory.RequestRejected
                        or FailureCategory.ServerError
                        or FailureCategory.UnexpectedResponse
                    ? await ctx.Client.GetServerVersionAsync(ct)
                    : null;
                // #177: exit code 1 and the server's status are different facts, so they go in
                // different fields. StatusCode is 0 when the request never reached the server
                // (unreachable/timeout), which is not a status - omit it rather than emit 0.
                ctx.Output.WriteError(
                    1,
                    message,
                    result.StatusCode == 0 ? null : result.StatusCode,
                    result.Category.ToWire(),
                    serverVersion,
                    ctx.CommandName
                );
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
                    + "performs a write, which is not allowed under --readonly / UMBRACO_READONLY.",
                commandName: ctx.CommandName
            );
            return 2;
        }
        catch (SafetyRefusalException refused)
        {
            // The command checked and refused (e.g. deleting an in-use type without --force).
            // Nothing was changed, so it is an abort (2), like a missing --yes (#246).
            ctx.Output.WriteError(2, refused.Message, commandName: ctx.CommandName);
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
            ctx.Output.WriteError(1, ex.Message, commandName: ctx.CommandName);
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
        CancellationToken ct
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
            ctx.Output.WriteError(
                2,
                $"Could not read ids: {ex.Message}",
                commandName: ctx.CommandName
            );
            return 2;
        }

        // Nothing to do — surface an empty result rather than silently exiting.
        if (rawIds.Count == 0)
        {
            ctx.Output.WriteError(
                2,
                "No ids supplied. Provide ids via --file <path> or stdin.",
                commandName: ctx.CommandName
            );
            return 2;
        }

        // Destructive-op gate (#70), applied ONCE for the whole batch. Skipped under --dry-run
        // (previewed, not sent) and --readonly (refused at the HTTP layer) exactly as the
        // single-op path does. The prompt comes from the command's CommandSafety declaration.
        var confirmationPrompt = CommandSafety.PromptFor(parseResult);
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
                ctx.Output.WriteError(2, "Operation cancelled.", commandName: ctx.CommandName);
                return 2;
            }
        }

        var results = new List<BulkItemResult>(rawIds.Count);
        foreach (var raw in rawIds)
        {
            if (!Guid.TryParse(raw, out var id))
            {
                results.Add(new BulkItemResult(raw, BulkItemStatus.Error, "Not a valid GUID."));
                continue;
            }

            try
            {
                var result = await callPerId(ctx.Client, id, ct);
                if (result.IsSuccess)
                {
                    results.Add(new BulkItemResult(raw, BulkItemStatus.Success, null));
                }
                else
                {
                    results.Add(new BulkItemResult(raw, BulkItemStatus.Error, result.ErrorMessage));
                }
            }
            catch (DryRunException dry)
            {
                // Under --dry-run each write is aborted before it is sent; record the request it
                // would have been (#236) on the item, rather than printing N request envelopes.
                results.Add(
                    new BulkItemResult(
                        raw,
                        BulkItemStatus.DryRun,
                        null,
                        BulkRequest.From(dry.Method, dry.Url, dry.Body)
                    )
                );
            }
            catch (ReadOnlyModeException)
            {
                results.Add(
                    new BulkItemResult(raw, BulkItemStatus.Error, "Blocked by --readonly.")
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return 130; // Ctrl-C mid-batch
            }
            catch (Exception ex)
            {
                results.Add(new BulkItemResult(raw, BulkItemStatus.Error, ex.Message));
            }
        }

        ctx.Output.WriteBulk(results, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds);
        // One source for "did anything fail": the same summary the envelope status comes from.
        return BulkSummary.Of(results).ExitCode;
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
    /// Writes a fixed success message via <see cref="IOutputWriter.WriteMessage"/>. A command
    /// declared destructive with <see cref="CommandSafety.Destructive{TCommand}"/> is confirmed
    /// before it runs unless <c>--yes</c> is given (#70, #255).
    /// </summary>
    /// <typeparam name="T">The client call's payload type (discarded).</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="successMessage">The message written on success.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunMessageAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        string successMessage,
        CancellationToken ct
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
            ct
        );

    /// <summary>Projects the result into table rows via <see cref="IOutputWriter.WriteTable"/>.</summary>
    /// <summary>
    /// Runs a list command (#164/#173): structured output is serialized from the items
    /// themselves, the human table from <paramref name="headers"/> and <paramref name="row"/>,
    /// and <c>meta</c> carries what the source can say about the rest of the data.
    /// </summary>
    /// <typeparam name="T">The client result type.</typeparam>
    /// <typeparam name="TItem">The item type serialized to structured output.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="items">Selects the items from the result.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="paging">What the result can say about totals and offsets.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunListAsync<T, TItem>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Func<T?, IReadOnlyList<TItem>> items,
        string[] headers,
        Func<TItem, string[]> row,
        Func<T?, ListPaging> paging,
        CancellationToken ct
    ) =>
        RunAsync(
            parseResult,
            commandName,
            call,
            (ctx, data) =>
            {
                var list = items(data);

                // The human table's cells and its captions are declared separately, so nothing
                // but this checks they line up. A mismatch would silently shift every column.
                var cells = list.Select(row).ToList();
                if (cells.FirstOrDefault() is { } first && first.Length != headers.Length)
                    throw new InvalidOperationException(
                        $"{commandName} declares {headers.Length} column(s) but produced "
                            + $"{first.Length} cell(s) per row."
                    );

                ctx.Output.WriteList(
                    [.. list.Cast<object>()],
                    headers,
                    cells,
                    paging(data),
                    ctx.CommandName,
                    ctx.Stopwatch.ElapsedMilliseconds
                );
            },
            ct
        );

    /// <summary>
    /// Runs a paged list command - the common case, where the client returns a
    /// <see cref="PagedResponse{TItem}"/> and the caller knows its own skip/take.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="skip">The offset requested, for <c>meta.skip</c>.</param>
    /// <param name="take">The page size requested, for <c>meta.take</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunPagedAsync<TItem>(
        ParseResult parseResult,
        string commandName,
        Func<
            IUmbracoManagementClient,
            int,
            int,
            CancellationToken,
            Task<UmbracoResponse<PagedResponse<TItem>>>
        > call,
        string[] headers,
        Func<TItem, string[]> row,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        // The call receives the same skip/take that reach meta, so the two cannot drift - a
        // caller cannot report a page the server was never asked for.
        RunListAsync(
            parseResult,
            commandName,
            (client, c) => call(client, skip, take, c),
            d => (d?.Items ?? []).ToList(),
            headers,
            row,
            d => new ListPaging(d?.Total, skip, take),
            ct
        );

    /// <summary>
    /// Runs a list command whose source returns everything it has, so there is nothing to page.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunCompleteListAsync<TItem>(
        ParseResult parseResult,
        string commandName,
        Func<
            IUmbracoManagementClient,
            CancellationToken,
            Task<UmbracoResponse<IReadOnlyList<TItem>>>
        > call,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) => RunWholeListAsync(parseResult, commandName, call, d => d, headers, row, ct);

    /// <summary>
    /// <see cref="RunCompleteListAsync{TItem}(ParseResult, string, Func{IUmbracoManagementClient, CancellationToken, Task{UmbracoResponse{IReadOnlyList{TItem}}}}, string[], Func{TItem, string[]}, CancellationToken)"/>
    /// for clients that return an <see cref="IEnumerable{T}"/> rather than a list.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunCompleteListAsync<TItem>(
        ParseResult parseResult,
        string commandName,
        Func<
            IUmbracoManagementClient,
            CancellationToken,
            Task<UmbracoResponse<IEnumerable<TItem>>>
        > call,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) => RunWholeListAsync(parseResult, commandName, call, d => d, headers, row, ct);

    /// <summary>Shared core for the two complete-list overloads.</summary>
    /// <typeparam name="T">The client result type.</typeparam>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="select">Selects the sequence from the result.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    private Task<int> RunWholeListAsync<T, TItem>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Func<T?, IEnumerable<TItem>?> select,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) =>
        RunListAsync(
            parseResult,
            commandName,
            call,
            d => select(d)?.ToList() ?? [],
            headers,
            row,
            // Unknown, not complete. These sources return what they return; whether the server
            // capped it is not something the response says, and claiming otherwise is the same
            // silent-completeness assertion #173 exists to end.
            _ => ListPaging.Unknown,
            ct
        );

    /// <summary>
    /// Renders a computed row report as a table.
    /// <para>
    /// For a list of entities use <see cref="RunPagedAsync"/> or
    /// <see cref="RunCompleteListAsync{TItem}(ParseResult, string, Func{IUmbracoManagementClient, CancellationToken, Task{UmbracoResponse{IReadOnlyList{TItem}}}}, string[], Func{TItem, string[]}, CancellationToken)"/>,
    /// which serialize the DTOs so structured output matches the matching <c>get</c> (#164).
    /// This remains for results that are <i>not</i> a list of entities - <c>content diff</c> and
    /// <c>schema diff</c>, whose rows are computed from a comparison and have no DTO to serialize
    /// - where deriving the keys from the captions is the only shape there is.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The client result type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="commandName">The dotted command name.</param>
    /// <param name="call">The client call.</param>
    /// <param name="headers">Column headers, camelCased into field keys by structured writers.</param>
    /// <param name="rows">Projects the result into rows.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
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
