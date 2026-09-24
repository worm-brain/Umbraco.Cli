using System.CommandLine;
using Umbraco.Cli.Client;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Wires <c>content domains</c> (#180) - Culture and Hostnames.
/// <para>
/// Nothing in the CLI covered domains, so a multilingual site could be built and published and
/// still be unreachable in every culture but the default: Umbraco logs "the root node was
/// published with multiple cultures, but no domains are configured" and serves nothing for the
/// rest. The 2026-09-23 round hit exactly that and had to call the Management API directly.
/// </para>
/// </summary>
public static class ContentDomainsCommand
{
    /// <summary>Builds the <c>domains</c> sub-noun.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    public static Command Build(CommandExecutor executor)
    {
        var cmd = new Command(
            "domains",
            "Read and set a document's Culture and Hostnames bindings."
        );
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildSet(executor));
        return cmd;
    }

    /// <summary>Builds <c>content domains get</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command(
            "get",
            "Show a document's domains.\n\nExample:\n  umbraco content domains get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Document ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content.domains.get",
                    (client, c) => client.GetDomainsAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds <c>content domains set</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildSet(CommandExecutor executor)
    {
        var cmd = new Command(
            "set",
            "Set a document's domains.\n\nThe API replaces the whole set, so --domain adds to what is already there rather than replacing it; pass --replace to set exactly what you name and drop the rest.\n\nExamples:\n  umbraco content domains set <id> --default en-US --domain example.com=en-US --domain example.com/da=da-DK\n  umbraco content domains set <id> --replace --domain example.com=en-US"
        );
        var idArg = new Argument<Guid>("id") { Description = "Document ID." };
        var defaultOpt = new Option<string?>("--default")
        {
            Description =
                "ISO code served when no domain matches. Omit to leave the current default alone.",
        };
        var domainOpt = new Option<string[]>("--domain")
        {
            Description = "A binding as host=isoCode, e.g. example.com/da=da-DK. Repeatable.",
            AllowMultipleArgumentsPerToken = true,
        };
        var replaceOpt = new Option<bool>("--replace")
        {
            Description =
                "Set exactly the domains given, removing any others. Without it they are merged in.",
        };
        cmd.Add(idArg);
        cmd.Add(defaultOpt);
        cmd.Add(domainOpt);
        cmd.Add(replaceOpt);

        cmd.Validators.Add(result =>
        {
            var domains = result.GetValue(domainOpt) ?? [];
            if (domains.Length == 0 && string.IsNullOrEmpty(result.GetValue(defaultOpt)))
                result.AddError("Supply --domain and/or --default; there is nothing to set.");
            if (domains.Any(d => !d.Contains('=')))
                result.AddError("Each --domain must be host=isoCode, e.g. example.com/da=da-DK.");
        });

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    "content.domains.set",
                    async (client, c) =>
                    {
                        var id = parseResult.GetValue(idArg);
                        var requested = (parseResult.GetValue(domainOpt) ?? [])
                            .Select(d => d.Split('=', 2))
                            .Select(p => new DomainBinding { DomainName = p[0], IsoCode = p[1] })
                            .ToList();

                        // The PUT replaces the whole set, so read first and merge by hostname -
                        // otherwise adding one binding silently drops every other one, which is
                        // #178's failure in a different noun.
                        var merged = requested;
                        var defaultIso = parseResult.GetValue(defaultOpt);
                        if (!parseResult.GetValue(replaceOpt))
                        {
                            var current = await client.GetDomainsAsync(id, c);
                            if (!current.IsSuccess)
                                return UmbracoResponse<DomainsResponse>.Failure(
                                    current.StatusCode,
                                    current.ErrorMessage!,
                                    current.Category
                                );

                            merged = [.. current.Data!.Domains];
                            foreach (var binding in requested)
                            {
                                var existing = merged.FindIndex(m =>
                                    string.Equals(
                                        m.DomainName,
                                        binding.DomainName,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                );
                                if (existing >= 0)
                                    merged[existing] = binding;
                                else
                                    merged.Add(binding);
                            }

                            defaultIso ??= current.Data!.DefaultIsoCode;
                        }

                        return await client.SetDomainsAsync(
                            id,
                            new SetDomainsRequest { DefaultIsoCode = defaultIso, Domains = merged },
                            c
                        );
                    },
                    ct
                )
        );

        return cmd;
    }
}
