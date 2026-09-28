using System.CommandLine;
using System.CommandLine.Parsing;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// The shared <c>--json-body</c> / <c>--schema</c> (and optional <c>--example</c>) options used by
/// write commands that accept a full JSON request body as an alternative to typed flags.
/// Centralises the declarations and the "is schema requested / is a body given / read the body"
/// plumbing so each command does not re-implement it; the flag-specific validation and request
/// construction stay in the command, as they differ per verb.
/// <para>
/// <c>--schema</c> means one thing everywhere: print the body's JSON Schema, offline
/// (docs/conventions.md 4.1). <c>--example</c>, where a schema cannot say enough, prints a real
/// item from the instance and so needs a host.
/// </para>
/// </summary>
public sealed class JsonBodyOption
{
    private readonly Option<string?> _body;
    private readonly Option<bool> _schema;
    private readonly Option<bool>? _example;

    /// <summary>Builds the options.</summary>
    /// <param name="bodyDescription">Help text for <c>--json-body</c> (varies per verb).</param>
    /// <param name="exampleDescription">Help text for <c>--example</c>; null leaves the option out.</param>
    public JsonBodyOption(string bodyDescription, string? exampleDescription = null)
    {
        _body = new Option<string?>("--json-body") { Description = bodyDescription };
        _schema = new Option<bool>("--schema")
        {
            Description =
                "Print the JSON Schema of the --json-body request and exit (no host needed).",
        };
        if (exampleDescription is not null)
            _example = new Option<bool>("--example") { Description = exampleDescription };
    }

    /// <summary>
    /// Declares <c>--json-body</c> required unless one of <paramref name="alternatives"/> is given,
    /// for the catalog's <c>requiredUnless</c> (#84). The command's validator still enforces it.
    /// </summary>
    /// <param name="alternatives">The option names that make the body unnecessary.</param>
    /// <returns>This instance, for chaining.</returns>
    public JsonBodyOption BodyRequiredUnless(params string[] alternatives)
    {
        _body.RequiredUnless(alternatives);
        return this;
    }

    /// <summary>Adds the options to a command.</summary>
    /// <param name="cmd">The command to add the options to.</param>
    public void AddTo(Command cmd)
    {
        cmd.Add(_body);
        cmd.Add(_schema);
        if (_example is not null)
            cmd.Add(_example);
    }

    /// <summary>Whether <c>--schema</c> was given (a local describe-and-exit, like <c>--help</c>).</summary>
    /// <param name="result">The parsed command line.</param>
    /// <returns>True if the schema should be printed instead of running the command.</returns>
    public bool SchemaRequested(ParseResult result) => result.GetValue(_schema);

    /// <summary>Whether <c>--schema</c> was given. Overload for use inside a command validator.</summary>
    /// <param name="result">The command result passed to a validator.</param>
    /// <returns>True if the schema should be printed instead of running the command.</returns>
    public bool SchemaRequested(CommandResult result) => result.GetValue(_schema);

    /// <summary>Whether <c>--example</c> was given; always false when the command has no such option.</summary>
    /// <param name="result">The parsed command line.</param>
    /// <returns>True if a real item should be printed instead of running the command.</returns>
    public bool ExampleRequested(ParseResult result) =>
        _example is not null && result.GetValue(_example);

    /// <summary>Whether <c>--example</c> was given. Overload for use inside a command validator.</summary>
    /// <param name="result">The command result passed to a validator.</param>
    /// <returns>True if a real item should be printed instead of running the command.</returns>
    public bool ExampleRequested(CommandResult result) =>
        _example is not null && result.GetValue(_example);

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
    /// <see cref="HasBody(ParseResult)"/> is true; read inside the executor's try so a missing file
    /// surfaces as a clean error.
    /// </summary>
    /// <param name="result">The parsed command line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw JSON body text.</returns>
    public Task<string> ReadAsync(ParseResult result, CancellationToken ct) =>
        JsonBodyInput.ReadAsync(result.GetValue(_body)!, ct);
}
