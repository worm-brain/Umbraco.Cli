using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Api;

/// <summary>
/// The <c>api</c> noun (ADR 0010): a raw request to the site's own routes that keeps every
/// guardrail a built-in command has, so a script, an agent or an extension command need not
/// fetch a token and call the API directly. One verb per HTTP method, so safety is declared per
/// command as everywhere else (docs/conventions.md 5.2): <c>get</c> is a read, <c>post</c>,
/// <c>put</c> and <c>patch</c> are writes, <c>delete</c> is destructive. That also lets an
/// allow-list permit <c>api.get</c> without permitting writes.
/// </summary>
public static class ApiCommand
{
    /// <summary>Builds the <c>api</c> noun with its five verbs.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "api",
            "Send a request to the site's own API, with every guardrail applied.\n\n"
                + "The path is from the site's root and starts /umbraco/: the Management API, the "
                + "Delivery API or a package's own routes, and nothing on any other host. The "
                + "request carries the configured login, and --readonly, --dry-run, the allow-list "
                + "and token refresh apply as on every command. The response body is the "
                + "envelope's data, or {} when there is none."
        ).WithExamples(
            "umbraco api get /umbraco/management/api/v1/server/status",
            "umbraco api post /umbraco/management/api/v1/language --json-body language.json --dry-run"
        );

        cmd.Add(
            Verb(
                    executor,
                    HttpMethod.Get,
                    "Get a path under /umbraco/ and print the response body."
                )
                .WithExamples(
                    "umbraco api get /umbraco/management/api/v1/server/status",
                    "umbraco api get \"/umbraco/management/api/v1/tree/document/root?skip=0&take=10\"",
                    "umbraco api get /umbraco/management/api/v1/document/<id> | jq .data.values"
                )
        );
        cmd.Add(
            Verb(
                    executor,
                    HttpMethod.Post,
                    "Post to a path under /umbraco/, with an optional JSON body."
                )
                .WithExamples(
                    "umbraco api post /umbraco/management/api/v1/language --json-body language.json",
                    "umbraco api post /umbraco/management/api/v1/language --json-body language.json --dry-run",
                    "cat language.json | umbraco api post /umbraco/management/api/v1/language --json-body -"
                )
        );
        cmd.Add(
            Verb(
                    executor,
                    HttpMethod.Put,
                    "Put to a path under /umbraco/, with an optional JSON body."
                )
                .WithExamples(
                    "umbraco api put /umbraco/management/api/v1/language/da-DK --json-body language.json",
                    "umbraco api put /umbraco/management/api/v1/document/<id>/move-to-recycle-bin"
                )
        );
        cmd.Add(
            Verb(
                    executor,
                    HttpMethod.Patch,
                    "Patch a path under /umbraco/, with an optional JSON body."
                )
                .WithExamples(
                    "umbraco api patch /umbraco/my-package/api/v1/item/<id> --json-body changes.json"
                )
        );
        cmd.Add(
            Verb(executor, HttpMethod.Delete, "Delete the resource at a path under /umbraco/.")
                .WithExamples(
                    "umbraco api delete /umbraco/management/api/v1/language/da-DK --yes",
                    "umbraco api delete /umbraco/management/api/v1/language/da-DK --dry-run"
                )
        );
        return cmd;
    }

    /// <summary>
    /// Builds one verb: the <c>path</c> argument (checked at parse time by
    /// <see cref="PassthroughPath"/>, so a bad path fails before any login), <c>--json-body</c> on
    /// every method but GET, and the method's safety declaration.
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="method">The HTTP method the verb sends, and whose lower-case name it has.</param>
    /// <param name="summary">The help summary.</param>
    /// <returns>The verb.</returns>
    private static Command Verb(CommandExecutor executor, HttpMethod method, string summary)
    {
        var read = method == HttpMethod.Get;
        var cmd = new Command(
            method.Method.ToLowerInvariant(),
            read
                ? summary
                : summary + "\n\nA write: --readonly refuses it and --dry-run previews it."
        );

        var path = new Argument<string>("path")
        {
            Description =
                "The path from the site's root, starting /umbraco/, with any query string "
                + "(quote it in a shell when it has one).",
        };
        path.Validators.Add(result =>
        {
            if (PassthroughPath.Problem(result.GetValueOrDefault<string>()) is { } problem)
                result.AddError(problem);
        });
        cmd.Add(path);

        // No --schema beside it (docs/conventions.md 9): the body's shape is whatever the path
        // takes, which the CLI has no schema for.
        Option<string?>? body = null;
        if (!read)
        {
            body = new Option<string?>("--json-body")
            {
                Description =
                    "Path to a JSON file (or - for stdin) sent as the request body, as it is.",
            };
            cmd.Add(body);
        }

        if (method == HttpMethod.Delete)
            cmd.Destructive(p =>
                $"Send DELETE {p.GetValue(path)} to the site? The CLI cannot undo it."
            );
        else if (!read)
            cmd.Mutating();

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                        await client.SendRawAsync(
                            method,
                            parseResult.GetValue(path)!,
                            await ReadBodyAsync(parseResult, body, c),
                            c
                        ),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>
    /// Reads and parses <c>--json-body</c>, inside the executor so a missing file or bad JSON is an
    /// <c>invalid_argument</c> error like any other input the caller must fix.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="body">The verb's <c>--json-body</c> option; null for GET.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The body, or null when none was given.</returns>
    /// <exception cref="InvalidInputException">The body is not valid JSON.</exception>
    private static async Task<JsonNode?> ReadBodyAsync(
        ParseResult parseResult,
        Option<string?>? body,
        CancellationToken ct
    )
    {
        if (body is null || parseResult.GetValue(body) is not { Length: > 0 } source)
            return null;
        var text = await JsonBodyInput.ReadAsync(source, ct);
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidInputException($"--json-body is not valid JSON: {ex.Message}");
        }
    }
}
