using System.CommandLine;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// Wires <c>content domain</c> (#180) - Culture and Hostnames.
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
            "domain",
            "Read and set a document's Culture and Hostnames bindings."
        );
        cmd.Add(BuildGet(executor));
        cmd.Add(BuildSet(executor));
        return cmd;
    }

    /// <summary>Builds <c>content domain get</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildGet(CommandExecutor executor)
    {
        var cmd = new Command("get", "Get a document's domains.").WithExamples(
            "umbraco content domain get 3f7a8b2e-..."
        );
        var idArg = new Argument<Guid>("id") { Description = "Document ID." };
        cmd.Add(idArg);
        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    (client, c) => client.GetDomainsAsync(parseResult.GetValue(idArg), c),
                    ct
                )
        );
        return cmd;
    }

    /// <summary>Builds <c>content domain set</c>.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <returns>The configured command.</returns>
    private static Command BuildSet(CommandExecutor executor)
    {
        // A PUT under a verb the catalog's verb set does not know.
        var cmd = new Command(
            "set",
            "Set a document's domains.\n\nThe API replaces the whole set, so --domain adds to what is already there rather than replacing it; pass --replace to set exactly what you name and drop the rest."
        )
            .WithExamples(
                "umbraco content domain set <id> --default-culture en-US --domain example.com=en-US --domain example.com/da=da-DK",
                "umbraco content domain set <id> --replace --domain example.com=en-US"
            )
            .Mutating();
        var idArg = new Argument<Guid>("id") { Description = "Document ID." };
        var defaultOpt = new Option<string?>("--default-culture")
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
        // Replacing drops whatever is not given, which the CLI cannot restore (docs/conventions.md 5.2).
        cmd.DestructiveWith(
            replaceOpt,
            parseResult =>
                $"Replace every domain of {parseResult.GetValue(idArg)}, removing the ones not given?"
        );
        cmd.Add(idArg);
        cmd.Add(defaultOpt);
        cmd.Add(domainOpt);
        cmd.Add(replaceOpt);

        cmd.Validators.Add(result =>
        {
            var domains = result.GetValue(domainOpt) ?? [];
            if (domains.Length == 0 && string.IsNullOrEmpty(result.GetValue(defaultOpt)))
                result.AddError(
                    "Supply --domain and/or --default-culture; there is nothing to set."
                );
        });
        KeyValuePairs.Validate(
            cmd,
            domainOpt,
            "--domain must be host=isoCode, e.g. example.com/da=da-DK"
        );

        cmd.SetAction(
            (parseResult, ct) =>
                executor.RunObjectAsync(
                    parseResult,
                    async (client, c) =>
                    {
                        var id = parseResult.GetValue(idArg);
                        var requested = KeyValuePairs
                            .Parse(parseResult.GetValue(domainOpt))
                            .Select(p => new DomainBinding
                            {
                                DomainName = p.Key,
                                IsoCode = p.Value,
                            })
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
                                return UmbracoResponse<DomainsResponse>.FailureFrom(current);

                            merged = MergeByKey.Upsert(
                                current.Data!.Domains,
                                requested,
                                b => b.DomainName,
                                StringComparer.OrdinalIgnoreCase
                            );

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
