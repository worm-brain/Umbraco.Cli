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
    /// Runs one client call and renders its result. Exit codes are <see cref="ExitCode"/>; every
    /// error carries a category. Note a <c>--readonly</c> block can fire after prerequisite reads
    /// (e.g. alias→id resolution) have already run.
    /// </summary>
    /// <typeparam name="T">The client call's payload type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call.</param>
    /// <param name="render">Renders the payload on success.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunAsync<T>(
        ParseResult parseResult,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Action<CommandContext, T?> render,
        CancellationToken ct
    ) =>
        // The vast majority of commands are a single client call; expose the client-only shape
        // and delegate to the context-aware core below.
        RunContextualAsync(parseResult, (ctx, c) => call(ctx.Client, c), render, ct);

    /// <summary>
    /// Context-aware variant of <see cref="RunAsync{T}"/>: the operation receives the whole
    /// <see cref="CommandContext"/> (not just the client), so a command that must branch on
    /// <see cref="CommandContext.DryRun"/> / <see cref="CommandContext.ReadOnly"/> before doing
    /// its work can. Used by <c>schema apply</c>, whose <c>--dry-run</c> previews a *plan* rather
    /// than the raw requests the mutation interceptor records. Shares the identical context-build,
    /// confirmation gate, error mapping, dry-run preview and exit-code handling.
    /// </summary>
    /// <typeparam name="T">The rendered payload type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The operation, receiving the built context.</param>
    /// <param name="render">Renders the payload on success.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunContextualAsync<T>(
        ParseResult parseResult,
        Func<CommandContext, CancellationToken, Task<UmbracoResponse<T>>> call,
        Action<CommandContext, T?> render,
        CancellationToken ct
    )
    {
        CommandContext ctx;
        try
        {
            ctx = await _factory.CreateAsync(parseResult, ct);
        }
        catch (CommandAbortedException)
        {
            return (int)ExitCode.Aborted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (int)ExitCode.Cancelled;
        }

        // Destructive-op gate (#70): a command declared destructive (CommandSafety, #255) must be
        // confirmed before it runs, unless --yes was given. Non-interactively (piped/scripted/
        // agent) we never prompt — we abort and require --yes, so a destructive op can never
        // happen silently. Skipped under --dry-run (the mutation is previewed, not sent) and
        // under --readonly (the write will be refused at the HTTP layer), so we never prompt to
        // confirm an operation that isn't going to execute anyway.
        // Pre-flight refusal (#246, #253): e.g. deleting a type that content still uses. Checked
        // before the confirmation prompt, so nobody confirms a delete only to have it refused. It
        // also applies under --dry-run: the refusal is exactly what a real run would do.
        string? refusal;
        try
        {
            refusal = await CommandSafety.RefusalFor(parseResult, ctx.Client, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (int)ExitCode.Cancelled;
        }
        if (refusal is not null)
            return Abort(ctx, FailureCategory.Refused, refusal);

        if (Confirm(ctx, parseResult, "a destructive operation") is { } declined)
            return declined;

        try
        {
            var result = await call(ctx, ct);

            // --dry-run: the interceptor recorded the writes instead of sending them, and answered
            // each with a fake success. Whatever the call made of those answers (a read-back of an
            // item that was never created fails), what matters is the requests: preview them,
            // exit 0 - nothing was changed.
            if (WritePreview(ctx))
                return (int)ExitCode.Success;

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
                // #154: a response the client could not read points at the version check, which
                // the client (the API boundary) leaves to the CLI to name.
                if (result.Category is FailureCategory.UnexpectedResponse)
                    message += DriftHint(serverVersion);
                // #177: exit code 1 and the server's status are different facts, so they go in
                // different fields. StatusCode is 0 when the request never reached the server
                // (unreachable/timeout), which is not a status - omit it rather than emit 0.
                ctx.Output.WriteError(
                    ExitCode.Failed,
                    CategoryOf(result),
                    message,
                    ctx.CommandName,
                    result.StatusCode == 0 ? null : result.StatusCode,
                    serverVersion,
                    result.Details
                );
                return (int)ExitCode.Failed;
            }

            // The command's own meta fields (#440), read only now that the call has succeeded, so
            // a failure or a dry run never pays for them. Most commands declare none.
            await CommandMeta.AddToAsync(parseResult, ctx.Client, ctx.Output, ct);
            render(ctx, result.Data);
            return (int)ExitCode.Success;
        }
        catch (ReadOnlyModeException ro)
        {
            // --readonly: the mutation-interceptor refused a write. Report a clear error and a
            // non-zero exit; nothing was changed.
            return Abort(
                ctx,
                FailureCategory.ReadOnly,
                $"Read-only mode is active ({ro.Method} {ro.Url} was blocked). This command "
                    + "performs a write, which is not allowed under --readonly / UMBRACO_READONLY."
            );
        }
        catch (SafetyRefusalException refused)
        {
            // The command checked and refused (e.g. deleting an in-use type without --force).
            // Nothing was changed, so it is an abort, like a missing --yes (#246).
            return Abort(ctx, FailureCategory.Refused, refused.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (int)ExitCode.Cancelled; // distinct from a config abort
        }
        catch (Exception ex)
        {
            // A dry-run step that tripped over a fake response (a missing body) still previews
            // the writes recorded before it.
            if (WritePreview(ctx))
                return (int)ExitCode.Success;

            // Backstop: nothing escapes as a raw stack trace. Input the caller must fix (a
            // malformed --json-body, a file that is not there) is invalid_argument (#256);
            // anything else is a CLI bug, and says so.
            ctx.Output.WriteError(ExitCode.Failed, CategoryOf(ex), ex.Message, ctx.CommandName);
            return (int)ExitCode.Failed;
        }
    }

    /// <summary>
    /// Writes the <c>--dry-run</c> preview of every write the command would have sent (#353): the
    /// first request, then the rest in order. Does nothing when no write was recorded.
    /// </summary>
    /// <param name="ctx">The command context, whose <see cref="CommandContext.Previewed"/> holds the recorded writes.</param>
    /// <returns>True when a preview was written, so the caller should exit 0.</returns>
    private static bool WritePreview(CommandContext ctx)
    {
        if (ctx.Previewed.Count == 0)
            return false;
        var first = ctx.Previewed[0];
        ctx.Output.WriteDryRun(
            first.Method,
            first.Url,
            first.Body,
            ctx.CommandName,
            ctx.Stopwatch.ElapsedMilliseconds,
            [.. ctx.Previewed.Skip(1)]
        );
        return true;
    }

    /// <summary>
    /// Asks for confirmation when the parsed command is destructive, unless <c>--yes</c>,
    /// <c>--dry-run</c> or <c>--readonly</c> means nothing destructive will happen. Never prompts
    /// non-interactively: it refuses, so a destructive operation can never happen silently (#70).
    /// </summary>
    /// <param name="ctx">The command context.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="what">What is being refused, for the message (e.g. "a destructive operation").</param>
    /// <returns>The exit code when the run must stop, or null to proceed.</returns>
    private int? Confirm(CommandContext ctx, ParseResult parseResult, string what)
    {
        var prompt = CommandSafety.PromptFor(parseResult);
        if (prompt is null || ctx.AssumeYes || ctx.DryRun || ctx.ReadOnly)
            return null;

        if (!_confirmation.IsInteractive)
            return Abort(
                ctx,
                FailureCategory.ConfirmationRequired,
                $"{prompt} Refusing to run {what} without confirmation. Re-run with --yes to "
                    + "proceed (required in non-interactive mode)."
            );

        return _confirmation.Confirm(prompt)
            ? null
            : Abort(ctx, FailureCategory.Cancelled, "Operation cancelled.");
    }

    /// <summary>Writes an abort (exit 2) with its category and returns the exit code.</summary>
    /// <param name="ctx">The command context.</param>
    /// <param name="category">Why the run stopped.</param>
    /// <param name="message">What happened and how to proceed.</param>
    /// <returns><see cref="ExitCode.Aborted"/>.</returns>
    private static int Abort(CommandContext ctx, FailureCategory category, string message)
    {
        ctx.Output.WriteError(ExitCode.Aborted, category, message, ctx.CommandName);
        return (int)ExitCode.Aborted;
    }

    /// <summary>
    /// The category for a failed client call. The client always classifies its failures; one that
    /// arrives unclassified is derived from its status, so the envelope never lacks a category.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="result">The failed response.</param>
    /// <returns>The category to report.</returns>
    internal static FailureCategory CategoryOf<T>(UmbracoResponse<T> result) =>
        result.Category is not FailureCategory.None ? result.Category
        : result.StatusCode is 0 ? FailureCategory.Unreachable
        : result.StatusCode >= 500 ? FailureCategory.ServerError
        : FailureCategory.RequestRejected;

    /// <summary>
    /// The sentence added to an <see cref="FailureCategory.UnexpectedResponse"/> error (#154).
    /// When the server version is known to be outside the tested range (#153) it says so, naming
    /// both; otherwise it points at <c>auth doctor</c>, which checks the version. Uses the version
    /// the error path already fetched, so it costs no extra request.
    /// </summary>
    /// <param name="serverVersion">The connected server's version, or null when unknown.</param>
    /// <returns>The hint, with a leading space.</returns>
    internal static string DriftHint(string? serverVersion) =>
        VersionSupport.Check(serverVersion) is VersionFit.Unsupported
            ? " " + VersionSupport.OutOfRangeMessage(serverVersion!)
            : " Run 'umbraco auth doctor' to check the instance's Umbraco version.";

    /// <summary>
    /// The category for an exception the backstop caught: input the caller must fix is
    /// <see cref="FailureCategory.InvalidArgument"/>, anything else <see cref="FailureCategory.Internal"/>.
    /// A <see cref="System.Text.Json.JsonException"/> here is the caller's JSON (a snapshot file, a
    /// <c>--json-body</c>): one from reading a response is caught by the client's request guard
    /// and reported as <see cref="FailureCategory.UnexpectedResponse"/> before it gets here (#154).
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns>The category to report.</returns>
    internal static FailureCategory CategoryOf(Exception ex) =>
        ex
            is InvalidInputException
                or System.Text.Json.JsonException
                or FileNotFoundException
                or DirectoryNotFoundException
            ? FailureCategory.InvalidArgument
            : FailureCategory.Internal;

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
    /// <param name="readIds">
    /// Reads the raw id lines from stdin/file. Invoked after the context is built so an IO error
    /// (e.g. a missing file) is reported through the resolved output writer.
    /// </param>
    /// <param name="callPerId">The client call to run for each parsed id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="prepare">
    /// Runs once before the first write with every id that parses, for a read the writes can share
    /// (bulk publish reads every document's cultures in one batch, #414). Its result is ignored, so
    /// it must be an optimisation each write can do without. Null for none.
    /// </param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunBulkAsync(
        ParseResult parseResult,
        Func<IReadOnlyList<string>> readIds,
        Func<
            IUmbracoManagementClient,
            Guid,
            CancellationToken,
            Task<UmbracoResponse<Empty>>
        > callPerId,
        CancellationToken ct,
        Func<IUmbracoManagementClient, IReadOnlyList<Guid>, CancellationToken, Task>? prepare = null
    )
    {
        CommandContext ctx;
        try
        {
            ctx = await _factory.CreateAsync(parseResult, ct);
        }
        catch (CommandAbortedException)
        {
            return (int)ExitCode.Aborted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (int)ExitCode.Cancelled;
        }

        // Read-only mode refuses writes, so a bulk write is blocked before it runs — mirror the
        // single-op contract (exit 2 with a clear error) rather than running the loop and
        // reporting every item as a failure (which would read as an API error, exit 1). Bulk
        // operations are all writes, so this applies to the whole group.
        if (ctx.ReadOnly)
            return Abort(
                ctx,
                FailureCategory.ReadOnly,
                "Read-only mode is active (--readonly / UMBRACO_READONLY). This bulk command "
                    + "performs writes, which are not allowed."
            );

        // Read the ids now that the output writer exists, so a missing/unreadable file becomes a
        // clean error rather than an unhandled exception.
        IReadOnlyList<string> rawIds;
        try
        {
            rawIds = readIds();
        }
        catch (Exception ex)
        {
            // The input could not be read: the caller's to fix, and nothing ran.
            ctx.Output.WriteError(
                ExitCode.Failed,
                FailureCategory.InvalidArgument,
                $"Could not read ids: {ex.Message}",
                ctx.CommandName
            );
            return (int)ExitCode.Failed;
        }

        // Nothing to do — surface an empty result rather than silently exiting.
        if (rawIds.Count == 0)
        {
            ctx.Output.WriteError(
                ExitCode.Failed,
                FailureCategory.InvalidArgument,
                "No ids supplied. Provide ids via --file <path> or stdin.",
                ctx.CommandName
            );
            return (int)ExitCode.Failed;
        }

        // Destructive-op gate (#70), applied ONCE for the whole batch. Skipped under --dry-run
        // (previewed, not sent) and --readonly (refused at the HTTP layer) exactly as the
        // single-op path does. The prompt comes from the command's CommandSafety declaration.
        if (Confirm(ctx, parseResult, "a destructive bulk operation") is { } declined)
            return declined;

        // The first write recorded under --dry-run since `before`, as a bulk item's request.
        BulkRequest? PreviewedSince(int before) =>
            ctx.Previewed.Count > before
                ? BulkRequest.From(
                    ctx.Previewed[before].Method,
                    ctx.Previewed[before].Url,
                    ctx.Previewed[before].Body
                )
                : null;

        // Batch reads the writes need (#414), once for every id that parses. Its outcome is not
        // checked: it only saves reads, and each write still reads what the batch did not cover.
        if (prepare is not null)
        {
            var valid = rawIds
                .Select(raw => Guid.TryParse(raw, out var id) ? id : (Guid?)null)
                .OfType<Guid>()
                .ToList();
            try
            {
                if (valid.Count > 0)
                    await prepare(ctx.Client, valid, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return (int)ExitCode.Cancelled;
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

            // Under --dry-run each write is recorded rather than sent; the first one this item
            // recorded goes on the item (#236), rather than printing N request envelopes.
            var previewedBefore = ctx.Previewed.Count;
            try
            {
                var result = await callPerId(ctx.Client, id, ct);
                if (PreviewedSince(previewedBefore) is { } dry)
                    results.Add(new BulkItemResult(raw, BulkItemStatus.DryRun, null, dry));
                else if (result.IsSuccess)
                    results.Add(new BulkItemResult(raw, BulkItemStatus.Success, null));
                else
                    results.Add(new BulkItemResult(raw, BulkItemStatus.Error, result.ErrorMessage));
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException
                    && PreviewedSince(previewedBefore) is not null
                )
            {
                // A dry-run step that tripped over a fake response still previews its write.
                results.Add(
                    new BulkItemResult(
                        raw,
                        BulkItemStatus.DryRun,
                        null,
                        PreviewedSince(previewedBefore)
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
                return (int)ExitCode.Cancelled; // Ctrl-C mid-batch
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
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        CancellationToken ct
    ) =>
        RunAsync(
            parseResult,
            call,
            static (ctx, data) =>
                ctx.Output.WriteSuccess(data, ctx.CommandName, ctx.Stopwatch.ElapsedMilliseconds),
            ct
        );

    /// <summary>
    /// Runs a write whose human output is a confirmation (delete, move, update...). The call returns
    /// the write's <c>data</c> - the resulting item, or an <see cref="ItemRef"/> naming what it acted
    /// on - which structured output emits, because every success has <c>data</c>
    /// (docs/conventions.md 6.2); the human writer shows <paramref name="successMessage"/>. A command
    /// declared destructive is confirmed before it runs unless <c>--yes</c> is given (#70, #255).
    /// </summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The write, returning its data (see <see cref="WriteResult"/>).</param>
    /// <param name="successMessage">The human confirmation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunMessageAsync<T>(
        ParseResult parseResult,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        string successMessage,
        CancellationToken ct
    ) =>
        RunAsync(
            parseResult,
            call,
            (ctx, data) =>
                ctx.Output.WriteMessage(
                    data!,
                    successMessage,
                    ctx.CommandName,
                    ctx.Stopwatch.ElapsedMilliseconds
                ),
            ct
        );

    /// <summary>
    /// Refuses, at compile time, a write that returns no data: every success has <c>data</c>
    /// (docs/conventions.md 6.2). Chain <see cref="WriteResult.Then{T}"/> or
    /// <see cref="WriteResult.ThenRead{T}"/> onto the call to say what it returns.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">A write that returns nothing.</param>
    /// <param name="successMessage">The human confirmation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always; the overload exists to fail compilation.</exception>
    [Obsolete(
        "Every success has data: chain .Then(ItemRef.Of(id)) or .ThenRead(...) onto the call.",
        error: true
    )]
    public Task<int> RunMessageAsync(
        ParseResult parseResult,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<Empty>>> call,
        string successMessage,
        CancellationToken ct
    ) => throw new NotSupportedException();

    /// <summary>
    /// Runs a list command (#164/#173): structured output is serialized from the items
    /// themselves, the human table from <paramref name="headers"/> and <paramref name="row"/>,
    /// and <c>meta</c> carries what the source can say about the rest of the data.
    /// </summary>
    /// <typeparam name="T">The client result type.</typeparam>
    /// <typeparam name="TItem">The item type serialized to structured output.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call.</param>
    /// <param name="items">Selects the items from the result.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="paging">What the result can say about totals and offsets.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunListAsync<T, TItem>(
        ParseResult parseResult,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Func<T?, IReadOnlyList<TItem>> items,
        string[] headers,
        Func<TItem, string[]> row,
        Func<T?, ListPaging> paging,
        CancellationToken ct
    ) =>
        RunAsync(
            parseResult,
            call,
            (ctx, data) =>
            {
                WriteRows(ctx, items(data), headers, row, paging(data));
            },
            ct
        );

    /// <summary>
    /// Runs a computed report whose rows are records, not the client's own list - <c>content
    /// diff</c> and <c>schema diff</c> (#229). The rows are serialized as they are, like any list,
    /// and the report is complete by construction, so <c>meta</c> says so
    /// (<see cref="ListPaging.Complete"/>).
    /// </summary>
    /// <typeparam name="T">The client result type.</typeparam>
    /// <typeparam name="TItem">The row type serialized to structured output.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call.</param>
    /// <param name="items">Selects the rows from the result.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one row into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunReportAsync<T, TItem>(
        ParseResult parseResult,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Func<T?, IReadOnlyList<TItem>> items,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) =>
        RunAsync(parseResult, call, (ctx, data) => WriteReport(ctx, items(data), headers, row), ct);

    /// <summary>
    /// Writes a complete computed report, for a command that renders inside its own
    /// <see cref="RunContextualAsync{T}"/> (<c>content apply</c>, <c>schema apply</c>).
    /// </summary>
    /// <typeparam name="TItem">The row type serialized to structured output.</typeparam>
    /// <param name="ctx">The command context.</param>
    /// <param name="items">The rows.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one row into human table cells.</param>
    /// <exception cref="InvalidOperationException">The cells do not match the headers.</exception>
    public static void WriteReport<TItem>(
        CommandContext ctx,
        IReadOnlyList<TItem> items,
        string[] headers,
        Func<TItem, string[]> row
    ) => WriteRows(ctx, items, headers, row, ListPaging.Complete(items.Count));

    /// <summary>Writes a list: the items to structured output, the cells to the human table.</summary>
    /// <exception cref="InvalidOperationException">The cells do not match the headers.</exception>
    private static void WriteRows<TItem>(
        CommandContext ctx,
        IReadOnlyList<TItem> items,
        string[] headers,
        Func<TItem, string[]> row,
        ListPaging paging
    )
    {
        // The human table's cells and its captions are declared separately, so nothing but this
        // checks they line up. A mismatch would silently shift every column.
        var cells = items.Select(row).ToList();
        if (cells.FirstOrDefault() is { } first && first.Length != headers.Length)
            throw new InvalidOperationException(
                $"{ctx.CommandName} declares {headers.Length} column(s) but produced "
                    + $"{first.Length} cell(s) per row."
            );

        ctx.Output.WriteList(
            [.. items.Cast<object>()],
            headers,
            cells,
            paging,
            ctx.CommandName,
            ctx.Stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Runs a paged list command - the common case, where the client returns a
    /// <see cref="PagedResponse{TItem}"/> and the caller knows its own skip/take. When the command
    /// was run with <c>--all</c> (<see cref="PagingOptions.AllRequested"/>) the executor pages
    /// through the whole collection itself instead (#196), and <c>meta</c> reports it complete.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call, given the skip and take to request.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="skip">The offset requested, for <c>meta.skip</c>. Ignored under <c>--all</c>.</param>
    /// <param name="take">The page size requested, for <c>meta.take</c>. Ignored under <c>--all</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunPagedAsync<TItem>(
        ParseResult parseResult,
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
        PagingOptions.AllRequested(parseResult)
            ? RunListAsync(
                parseResult,
                (client, c) => CollectAllPagesAsync(client, call, c),
                d => (d?.Items ?? []).ToList(),
                headers,
                row,
                // Every page was read, so the list is complete: total is what was collected and
                // hasMore is false.
                d => ListPaging.Complete(d?.Total ?? 0),
                ct
            )
            // The call receives the same skip/take that reach meta, so the two cannot drift - a
            // caller cannot report a page the server was never asked for.
            : RunListAsync(
                parseResult,
                (client, c) => call(client, skip, take, c),
                d => (d?.Items ?? []).ToList(),
                headers,
                row,
                d => new ListPaging(d?.Total, skip, take),
                ct
            );

    /// <summary>
    /// Pages through a collection for <c>--all</c> (#196): requests pages of
    /// <see cref="PagingOptions.DefaultTake"/> until one comes back short or the collected count
    /// reaches the reported total. Deliberately a real loop rather than one huge <c>take</c>, which
    /// would only move the silent cap further out.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="call">The paged client call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Every item, with <see cref="PagedResponse{T}.Total"/> set to how many there are; the first
    /// failed page's failure; or an <c>invalid_argument</c> failure when the collection is larger
    /// than <see cref="PagingOptions.MaxAllItems"/>, so nothing is ever quietly truncated.
    /// </returns>
    internal static async Task<UmbracoResponse<PagedResponse<TItem>>> CollectAllPagesAsync<TItem>(
        IUmbracoManagementClient client,
        Func<
            IUmbracoManagementClient,
            int,
            int,
            CancellationToken,
            Task<UmbracoResponse<PagedResponse<TItem>>>
        > call,
        CancellationToken ct
    )
    {
        const int pageSize = PagingOptions.DefaultTake;
        var collected = new List<TItem>();

        while (true)
        {
            var page = await call(client, collected.Count, pageSize, ct);
            if (!page.IsSuccess)
                return page;

            var items = (page.Data?.Items ?? []).ToList();
            var total = page.Data?.Total ?? 0;

            // Fail on the first page when the server already says it is too big, rather than
            // reading 10,000 items only to throw them away.
            if (total > PagingOptions.MaxAllItems)
                return TooMany(total);

            collected.AddRange(items);
            // A short page is the end. A total of 0 beside items means the source did not report
            // one (a hand-built page), so only a positive total can end the walk early.
            if (items.Count < pageSize || (total > 0 && collected.Count >= total))
                break;
            if (collected.Count >= PagingOptions.MaxAllItems)
                return TooMany(null);
        }

        return UmbracoResponse<PagedResponse<TItem>>.Success(
            new PagedResponse<TItem> { Items = collected, Total = collected.Count }
        );

        static UmbracoResponse<PagedResponse<TItem>> TooMany(int? total) =>
            UmbracoResponse<PagedResponse<TItem>>.Failure(
                0,
                $"--all stops at {PagingOptions.MaxAllItems} items and this list has "
                    + (total is { } t ? $"{t}" : "more")
                    + ". Page through it with --skip and --take instead.",
                FailureCategory.InvalidArgument
            );
    }

    /// <summary>
    /// Runs a list command whose source returns everything it has, so there is nothing to page.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunCompleteListAsync<TItem>(
        ParseResult parseResult,
        Func<
            IUmbracoManagementClient,
            CancellationToken,
            Task<UmbracoResponse<IReadOnlyList<TItem>>>
        > call,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) => RunWholeListAsync(parseResult, call, d => d, headers, row, ct);

    /// <summary>
    /// <see cref="RunCompleteListAsync{TItem}(ParseResult, string, Func{IUmbracoManagementClient, CancellationToken, Task{UmbracoResponse{IReadOnlyList{TItem}}}}, string[], Func{TItem, string[]}, CancellationToken)"/>
    /// for clients that return an <see cref="IEnumerable{T}"/> rather than a list.
    /// </summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    public Task<int> RunCompleteListAsync<TItem>(
        ParseResult parseResult,
        Func<
            IUmbracoManagementClient,
            CancellationToken,
            Task<UmbracoResponse<IEnumerable<TItem>>>
        > call,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) => RunWholeListAsync(parseResult, call, d => d, headers, row, ct);

    /// <summary>Shared core for the two complete-list overloads.</summary>
    /// <typeparam name="T">The client result type.</typeparam>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="call">The client call.</param>
    /// <param name="select">Selects the sequence from the result.</param>
    /// <param name="headers">Human table column headers.</param>
    /// <param name="row">Projects one item into human table cells.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process exit code.</returns>
    private Task<int> RunWholeListAsync<T, TItem>(
        ParseResult parseResult,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
        Func<T?, IEnumerable<TItem>?> select,
        string[] headers,
        Func<TItem, string[]> row,
        CancellationToken ct
    ) =>
        RunListAsync(
            parseResult,
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
}
