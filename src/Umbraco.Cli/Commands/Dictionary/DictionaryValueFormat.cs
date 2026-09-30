using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Dictionary;

/// <summary>
/// Reports the format the site stores dictionary translations in as <c>meta.valueFormat</c>
/// (#440): <c>text</c>, or whatever a package on the site declares (ADR 0009). It is information
/// for the caller, never a check - the dictionary commands store any value they are given.
/// </summary>
public static class DictionaryValueFormat
{
    /// <summary>The meta field the format is reported in.</summary>
    public const string MetaKey = "valueFormat";

    /// <summary>Declares that <paramref name="command"/> reports the site's dictionary value format.</summary>
    /// <param name="command">A dictionary command.</param>
    /// <returns>The same command.</returns>
    public static Command WithValueFormat(this Command command) =>
        command.WithMeta(MetaKey, async (client, ct) => await ReadAsync(client, ct));

    /// <summary>
    /// Reads the site's dictionary value format, printing any problem with the declarations (two
    /// packages disagreeing) as a warning on stderr, as the CLI's other warnings are. Shared with
    /// <c>schema export</c>, which records the format in the snapshot (#442).
    /// </summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The format, or null when the site's manifests could not be read.</returns>
    public static async Task<string?> ReadAsync(
        IUmbracoManagementClient client,
        CancellationToken ct
    )
    {
        var capabilities = await client.GetSiteCapabilitiesAsync(ct);
        foreach (var warning in capabilities.Warnings)
            Console.Error.WriteLine($"warning: {warning}");
        return capabilities.DictionaryValueFormat;
    }
}
