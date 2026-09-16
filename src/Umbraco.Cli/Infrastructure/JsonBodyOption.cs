using System.CommandLine;
using System.CommandLine.Parsing;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The shared <c>--json-body</c> / <c>--schema</c> option pair used by write commands that accept a
/// full JSON request body as an alternative to typed flags (content create/update, document-blueprint
/// create/update). Centralises the two option declarations and the "is schema requested / is a body
/// given / read the body" plumbing so each command does not re-implement it; the flag-specific
/// validation and request construction stay in the command, as they differ per verb.
/// </summary>
public sealed class JsonBodyOption
{
    private readonly Option<string?> _body;
    private readonly Option<bool> _schema;

    /// <summary>Builds the option pair.</summary>
    /// <param name="bodyDescription">Help text for <c>--json-body</c> (varies per verb).</param>
    public JsonBodyOption(string bodyDescription)
    {
        _body = new Option<string?>("--json-body") { Description = bodyDescription };
        _schema = new Option<bool>("--schema")
        {
            Description =
                "Print the JSON Schema for the --json-body request and exit (no host/auth needed).",
        };
    }

    /// <summary>Adds both options to a command.</summary>
    /// <param name="cmd">The command to add the options to.</param>
    public void AddTo(Command cmd)
    {
        cmd.Add(_body);
        cmd.Add(_schema);
    }

    /// <summary>Whether <c>--schema</c> was given (a local describe-and-exit, like <c>--help</c>).</summary>
    /// <param name="result">The parsed command line.</param>
    /// <returns>True if the schema should be printed instead of running the command.</returns>
    public bool SchemaRequested(ParseResult result) => result.GetValue(_schema);

    /// <summary>Whether <c>--schema</c> was given. Overload for use inside a command validator.</summary>
    /// <param name="result">The command result passed to a validator.</param>
    /// <returns>True if the schema should be printed instead of running the command.</returns>
    public bool SchemaRequested(CommandResult result) => result.GetValue(_schema);

    /// <summary>Whether a <c>--json-body</c> source was supplied.</summary>
    /// <param name="result">The parsed command line.</param>
    /// <returns>True if a body path (or <c>-</c> for stdin) was given.</returns>
    public bool HasBody(ParseResult result) => !string.IsNullOrEmpty(result.GetValue(_body));

    /// <summary>Whether a <c>--json-body</c> source was supplied. Overload for a command validator.</summary>
    /// <param name="result">The command result passed to a validator.</param>
    /// <returns>True if a body path (or <c>-</c> for stdin) was given.</returns>
    public bool HasBody(CommandResult result) => !string.IsNullOrEmpty(result.GetValue(_body));

    /// <summary>
    /// Reads the <c>--json-body</c> content (from the file, or stdin when <c>-</c>). Only valid when
    /// <see cref="HasBody"/> is true; read inside the executor's try so a missing file surfaces as a
    /// clean error.
    /// </summary>
    /// <param name="result">The parsed command line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body text.</returns>
    public Task<string> ReadAsync(ParseResult result, CancellationToken ct) =>
        JsonBodyInput.ReadAsync(result.GetValue(_body)!, ct);
}
