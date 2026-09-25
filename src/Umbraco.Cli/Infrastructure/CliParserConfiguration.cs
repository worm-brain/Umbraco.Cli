using System.CommandLine;

namespace Umbraco.Cli.Infrastructure;

/// <summary>
/// Builds the shared <see cref="ParserConfiguration"/> used to parse the CLI's command line, so
/// production (<c>Program</c>) and the parse tests exercise identical parsing behaviour.
/// </summary>
public static class CliParserConfiguration
{
    /// <summary>
    /// Creates the parser configuration for the CLI. Response-file token expansion is disabled
    /// (<see cref="ParserConfiguration.ResponseFileTokenReplacer"/> set to <c>null</c>) so that an
    /// option value beginning with <c>@</c> is passed through verbatim instead of being treated as
    /// an <c>@file</c> response-file directive.
    /// <para>
    /// Serilog / Umbraco log-viewer filter expressions commonly start with <c>@</c> (for example
    /// <c>@Level='Error'</c> or <c>@Exception is not null</c>). With response files enabled,
    /// <c>log-viewer list --filter "@Level='Error'"</c> would try to load a response file named
    /// <c>Level='Error'</c>, fail, and abort the whole parse with an error. Disabling the replacer
    /// fixes this CLI-wide, not just for log-viewer (issue #115). The CLI never reads its arguments
    /// from response files, so nothing depends on the feature.
    /// </para>
    /// </summary>
    /// <returns>A parser configuration with response-file expansion turned off.</returns>
    public static ParserConfiguration Create() => new() { ResponseFileTokenReplacer = null };
}
