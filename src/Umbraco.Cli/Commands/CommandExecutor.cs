using System.CommandLine;
using Umbraco.Cli.Client;
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

    public CommandExecutor(CommandContextFactory factory) => _factory = factory;

    /// <summary>
    /// Exit codes: <c>0</c> success · <c>1</c> API failure · <c>2</c> aborted before
    /// running (no host / not authenticated) · <c>130</c> cancelled (Ctrl-C).
    /// </summary>
    public async Task<int> RunAsync<T>(
        ParseResult parseResult,
        string commandName,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> call,
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

        try
        {
            var result = await call(ctx.Client, ct);
            if (!result.IsSuccess)
            {
                ctx.Output.WriteError(result.StatusCode, result.ErrorMessage!);
                return 1;
            }

            render(ctx, result.Data);
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

    /// <summary>Writes a fixed success message via <see cref="IOutputWriter.WriteMessage"/>.</summary>
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
            (ctx, _) => ctx.Output.WriteMessage(successMessage),
            ct
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
