using System.CommandLine;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Infrastructure;

namespace Umbraco.Cli.Commands;

/// <summary>
/// Wiring for the schema commands that accept a full Management API body (#161/#169, #250 Phase 5).
/// <para>
/// A document type's properties and groups, and a data type's editor configuration, are too
/// structured to express as flags - which is why authoring either one meant a
/// <c>schema export -&gt; jq -&gt; schema apply</c> round-trip through the whole instance. These
/// commands take the body directly and hand it to the raw client, so the shape a caller edits is
/// the shape the API defines, with nothing in between to drift. <c>get</c> prints that same shape,
/// and <c>update --json-body</c> merges it back (#201).
/// </para>
/// <para>
/// <c>--schema</c> prints the body's JSON Schema offline, as it does on every command
/// (docs/conventions.md 4.1). It comes from the Management API spec (<see cref="ApiBodySchema"/>),
/// never a hand-written copy. <c>--example</c> reads a real item off the instance as a worked
/// example, and so needs a host; on an instance with none to copy (#200) it prints a minimal body,
/// which a test ties to the spec.
/// </para>
/// <para>
/// Each schema verb states only its own flags. The <c>--json-body</c> / <c>--schema</c> /
/// <c>--example</c> / <c>--replace</c> options, their validation and the branch (schema, example,
/// body, flags) live here once, keyed by a <see cref="SchemaNoun"/>.
/// </para>
/// </summary>
public static class RawBodyCommand
{
    /// <summary>Adds the <c>--json-body</c> / <c>--schema</c> / <c>--example</c> options used by the raw schema verbs.</summary>
    /// <param name="cmd">The command to add them to.</param>
    /// <returns>The body option handle.</returns>
    public static JsonBodyOption AddBodyOptions(Command cmd)
    {
        var body = new JsonBodyOption(
            "Path to a JSON file (or - for stdin) containing the Management API body. --schema "
                + "prints its JSON Schema; --example prints a real one to start from.",
            "Print an existing item of this kind from the instance, as a worked example of the "
                + "--json-body shape, and exit. Requires a host."
        );
        body.AddTo(cmd);
        return body;
    }

    // ── get ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a schema <c>get</c>: resolves the reference and prints the item's verbatim Management
    /// API body, which is exactly the shape <c>update --json-body</c> takes back (#201).
    /// </summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="noun">The schema noun.</param>
    /// <param name="idArg">The <c>&lt;id|alias&gt;</c> argument.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    public static Task<int> RunGetAsync(
        CommandExecutor executor,
        ParseResult parseResult,
        SchemaNoun noun,
        ReferenceArgument idArg,
        CancellationToken ct
    ) =>
        executor.RunObjectAsync(
            parseResult,
            (client, c) =>
                idArg.WithResolvedAsync(
                    parseResult,
                    client,
                    id => client.GetSchemaRawAsync(noun.Kind, id, c),
                    c
                ),
            ct
        );

    // ── create ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds <c>--json-body</c> / <c>--schema</c> to a schema <c>create</c>, with the rule that the
    /// flag-built create needs <paramref name="required"/> unless a body (which carries them
    /// itself) or <c>--schema</c> (which builds nothing) is given.
    /// </summary>
    /// <param name="cmd">The command.</param>
    /// <param name="noun">The schema noun, for the message.</param>
    /// <param name="required">The flags the flag-built create needs.</param>
    /// <returns>The body option handle.</returns>
    public static JsonBodyOption AddCreateOptions(
        Command cmd,
        SchemaNoun noun,
        params Option<string>[] required
    )
    {
        var body = AddBodyOptions(cmd);
        cmd.Validators.Add(result =>
        {
            if (
                body.SchemaRequested(result)
                || body.ExampleRequested(result)
                || body.HasBody(result)
            )
                return;
            if (required.Any(o => string.IsNullOrEmpty(result.GetValue(o))))
                result.AddError(
                    $"Supply {Join(required.Select(o => o.Name))}, or a full body with --json-body. "
                        + $"Run with --example to print a real {noun.Singular} as a starting point."
                );
        });
        return body;
    }

    /// <summary>
    /// Runs a schema <c>create</c>: <c>--schema</c> prints the body's JSON Schema, <c>--example</c>
    /// a real item, a <c>--json-body</c> is POSTed raw with its id settled first (#204), and
    /// otherwise <paramref name="flagCreate"/> builds it from the flags.
    /// </summary>
    /// <typeparam name="T">What the flag-built create returns.</typeparam>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="noun">The schema noun.</param>
    /// <param name="body">The body option handle, from <see cref="AddCreateOptions"/>.</param>
    /// <param name="idOpt">The <c>--id</c> option.</param>
    /// <param name="flagCreate">The flag-built create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    public static Task<int> RunCreateAsync<T>(
        CommandExecutor executor,
        ParseResult parseResult,
        SchemaNoun noun,
        JsonBodyOption body,
        Option<Guid?> idOpt,
        Func<IUmbracoManagementClient, CancellationToken, Task<UmbracoResponse<T>>> flagCreate,
        CancellationToken ct
    )
    {
        if (body.SchemaRequested(parseResult))
            return PrintSchema(noun, update: false);
        if (body.ExampleRequested(parseResult))
            return RunExampleAsync(executor, parseResult, noun, ct);

        if (body.HasBody(parseResult))
            return executor.RunObjectAsync(
                parseResult,
                async (client, c) =>
                    await CreateAsync(
                        await ReadBodyAsync(body, parseResult, c),
                        parseResult.GetValue(idOpt),
                        (json, token) => client.CreateSchemaRawAsync(noun.Kind, json, token),
                        c
                    ),
                ct
            );

        return executor.RunObjectAsync(parseResult, flagCreate, ct);
    }

    // ── update ───────────────────────────────────────────────────────────────

    /// <summary>The options a schema <c>update</c> adds; see <see cref="AddUpdateOptions"/>.</summary>
    /// <param name="Id">The optional <c>&lt;id|alias&gt;</c> argument (optional so <c>--schema</c> and <c>--example</c> run without it).</param>
    /// <param name="Body">The body option handle.</param>
    /// <param name="Replace">The <c>--replace</c> option.</param>
    public sealed record UpdateOptions(
        Argument<string?> Id,
        JsonBodyOption Body,
        Option<bool> Replace
    );

    /// <summary>
    /// Adds the id argument, <c>--json-body</c> / <c>--schema</c> and <c>--replace</c> to a schema
    /// <c>update</c>, with the rule that an update needs the id (unless <c>--schema</c>) and, when
    /// the verb has no flags of its own, a body.
    /// </summary>
    /// <param name="cmd">The command. The id argument is added first, so it is positional 0.</param>
    /// <param name="noun">The schema noun, for help and messages.</param>
    /// <param name="hasFlags">Whether the verb can update from flags alone.</param>
    /// <returns>The added options.</returns>
    public static UpdateOptions AddUpdateOptions(Command cmd, SchemaNoun noun, bool hasFlags)
    {
        var id = new Argument<string?>("id")
        {
            Description =
                $"The {noun.Singular}'s id, or its {noun.Kind.KeyName()}. Required unless --schema or --example is used.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        cmd.Add(id);
        var body = AddBodyOptions(cmd);
        var replace = new Option<bool>("--replace")
        {
            Description =
                "Send --json-body as the whole item, instead of merging its top-level keys into the "
                + "current one. Keys the body leaves out are then removed or reset.",
        };
        // Replacing drops whatever is not given, which the CLI cannot restore (docs/conventions.md 5.2).
        cmd.DestructiveWith(
            replace,
            _ => $"Replace the whole {noun.Singular} with --json-body, removing what it leaves out?"
        );
        cmd.Add(replace);

        cmd.Validators.Add(result =>
        {
            if (body.SchemaRequested(result) || body.ExampleRequested(result))
                return;
            if (string.IsNullOrEmpty(result.GetValue(id)))
                result.AddError(
                    $"Supply the {noun.Singular}. Run with --example to print a real one as a starting point."
                );
            else if (!hasFlags && !body.HasBody(result))
                result.AddError(
                    $"Supply --json-body. Run with --example to print a real {noun.Singular} as a starting point."
                );
            else if (result.GetValue(replace) && !body.HasBody(result))
                result.AddError(
                    "--replace needs --json-body: it sends the body as the whole item."
                );
        });
        return new UpdateOptions(id, body, replace);
    }

    /// <summary>
    /// Runs a schema <c>update</c>: <c>--schema</c> prints the body's JSON Schema, <c>--example</c>
    /// a real item, a <c>--json-body</c> is merged into the item (or replaces it with
    /// <c>--replace</c>) through one read-modify-write, and otherwise <paramref name="flagUpdate"/>
    /// applies the flags to the resolved id.
    /// </summary>
    /// <typeparam name="T">What the flag-built update returns.</typeparam>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="noun">The schema noun.</param>
    /// <param name="options">The options from <see cref="AddUpdateOptions"/>.</param>
    /// <param name="message">The success message.</param>
    /// <param name="flagUpdate">The flag-built update, given the resolved id; null when the verb has no flags.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    public static Task<int> RunUpdateAsync<T>(
        CommandExecutor executor,
        ParseResult parseResult,
        SchemaNoun noun,
        UpdateOptions options,
        string message,
        Func<
            IUmbracoManagementClient,
            Guid,
            CancellationToken,
            Task<UmbracoResponse<T>>
        >? flagUpdate,
        CancellationToken ct
    )
    {
        if (options.Body.SchemaRequested(parseResult))
            return PrintSchema(noun, update: true);
        if (options.Body.ExampleRequested(parseResult))
            return RunExampleAsync(executor, parseResult, noun, ct);

        // The alias the rest of the noun accepts (#159) is resolved here, so a caller never has
        // to look an id up just to write back what they just read.
        var reference = parseResult.GetValue(options.Id)!;
        if (options.Body.HasBody(parseResult) || flagUpdate is null)
            return executor.RunMessageAsync(
                parseResult,
                async (client, c) =>
                {
                    var json = await ReadBodyAsync(options.Body, parseResult, c);
                    return await client.WithResolvedAsync(
                        noun.Kind,
                        reference,
                        // An update's data is the resulting item, as get prints it.
                        id =>
                            client
                                .MergeSchemaItemAsync(
                                    noun.Kind,
                                    id,
                                    json,
                                    parseResult.GetValue(options.Replace),
                                    c
                                )
                                .ThenRead(() => client.GetSchemaRawAsync(noun.Kind, id, c)),
                        c
                    );
                },
                message,
                ct
            );

        return executor.RunMessageAsync(
            parseResult,
            (client, c) =>
                client.WithResolvedAsync(
                    noun.Kind,
                    reference,
                    id =>
                        flagUpdate(client, id, c)
                            .ThenRead(() => client.GetSchemaRawAsync(noun.Kind, id, c)),
                    c
                ),
            message,
            ct
        );
    }

    /// <summary>A shorthand for a schema <c>update</c> that has no flags of its own.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="noun">The schema noun.</param>
    /// <param name="options">The options from <see cref="AddUpdateOptions"/>.</param>
    /// <param name="message">The success message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    public static Task<int> RunUpdateAsync(
        CommandExecutor executor,
        ParseResult parseResult,
        SchemaNoun noun,
        UpdateOptions options,
        string message,
        CancellationToken ct
    ) => RunUpdateAsync<Empty>(executor, parseResult, noun, options, message, null, ct);

    // ── --schema / --example ─────────────────────────────────────────────────

    /// <summary>
    /// Runs the <c>--schema</c> branch: prints the body's JSON Schema, from the Management API spec,
    /// with no host - a local describe-and-exit, like <c>--help</c>.
    /// </summary>
    /// <param name="noun">The schema noun.</param>
    /// <param name="update">True for the update body (every key optional, as it is merged).</param>
    /// <returns>Exit code 0.</returns>
    public static Task<int> PrintSchema(SchemaNoun noun, bool update)
    {
        ApiBodySchema.Print(noun.Kind, update);
        return Task.FromResult((int)ExitCode.Success);
    }

    /// <summary>Runs the <c>--example</c> branch: prints a real item of this kind off the instance.</summary>
    /// <param name="executor">The shared command executor.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="noun">The schema noun.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    public static Task<int> RunExampleAsync(
        CommandExecutor executor,
        ParseResult parseResult,
        SchemaNoun noun,
        CancellationToken ct
    ) => executor.RunObjectAsync(parseResult, (client, c) => ExampleAsync(client, noun, c), ct);

    /// <summary>
    /// Reads an existing item of this kind and returns it as a worked example for
    /// <c>--example</c>, or the built-in minimal body when the instance has none (#200).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="noun">The schema noun.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An example body, or a failure explaining why there is none.</returns>
    public static async Task<UmbracoResponse<JsonNode>> ExampleAsync(
        IUmbracoManagementClient client,
        SchemaNoun noun,
        CancellationToken ct
    )
    {
        var ids = await noun.ListIds(client, ct);
        if (!ids.IsSuccess)
            return UmbracoResponse<JsonNode>.FailureFrom(ids);

        // A fresh site has no types to show - which is exactly when the shape is most needed - so
        // fall back to a minimal body that carries the fields the create endpoint requires.
        if (ids.Data is not { Count: > 0 } found)
            return MinimalBody(noun.Kind) is { } minimal
                ? UmbracoResponse<JsonNode>.Success(minimal)
                : UmbracoResponse<JsonNode>.Failure(
                    404,
                    $"This instance has no {noun.Plural} to use as an example. Create one first, or "
                        + "write the body against the Umbraco Management API reference."
                );

        // The lowest id, so the same instance prints the same example every run - "whatever the
        // API returned first" is not reproducible, and --example output gets diffed.
        return await client.GetSchemaRawAsync(noun.Kind, found.Order().First(), ct);
    }

    /// <summary>
    /// The smallest valid create body for a schema kind, for <c>--example</c> on an instance that has
    /// none to copy (#200). Each carries exactly the properties the Management API spec marks as
    /// required on the create model, with empty collections where it wants a list.
    /// </summary>
    /// <param name="kind">The schema kind.</param>
    /// <returns>A fresh body, or null when there is no built-in one for the kind.</returns>
    internal static JsonNode? MinimalBody(EntityKind kind) =>
        kind switch
        {
            EntityKind.DocumentType => JsonNode.Parse(
                """
                { "alias": "myDocumentType", "name": "My document type", "icon": "icon-document",
                  "allowedAsRoot": true, "isElement": false, "variesByCulture": false, "variesBySegment": false,
                  "properties": [], "containers": [], "allowedDocumentTypes": [], "compositions": [],
                  "allowedTemplates": [], "cleanup": { "preventCleanup": false } }
                """
            ),
            EntityKind.MediaType => JsonNode.Parse(
                """
                { "alias": "myMediaType", "name": "My media type", "icon": "icon-picture",
                  "allowedAsRoot": true, "isElement": false, "variesByCulture": false, "variesBySegment": false,
                  "properties": [], "containers": [], "allowedMediaTypes": [], "compositions": [] }
                """
            ),
            EntityKind.MemberType => JsonNode.Parse(
                """
                { "alias": "myMemberType", "name": "My member type", "icon": "icon-user",
                  "allowedAsRoot": false, "isElement": false, "variesByCulture": false, "variesBySegment": false,
                  "properties": [], "containers": [], "compositions": [] }
                """
            ),
            EntityKind.DataType => JsonNode.Parse(
                """
                { "name": "My text", "editorAlias": "Umbraco.TextBox",
                  "editorUiAlias": "Umb.PropertyEditorUi.TextBox", "values": [] }
                """
            ),
            _ => null,
        };

    // ── body handling ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>--json-body</c> source and parses it, failing with a message that says which
    /// input was bad rather than a bare parser exception.
    /// </summary>
    /// <param name="body">The body option handle.</param>
    /// <param name="parseResult">The parsed command line.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed body.</returns>
    /// <exception cref="InvalidOperationException">The input is not valid JSON.</exception>
    public static async Task<JsonNode> ReadBodyAsync(
        JsonBodyOption body,
        ParseResult parseResult,
        CancellationToken ct
    ) =>
        JsonNode.Parse(await body.ReadAsync(parseResult, ct))
        ?? throw new InvalidInputException(
            "--json-body did not contain a JSON object. Run with --example to print a real one."
        );

    /// <summary>
    /// Creates an item from a <c>--json-body</c> and reports its id (#204, #218). The create
    /// endpoints return no body, so the id is settled <b>before</b> the POST and written into the
    /// body (Umbraco 14+ honours a client-supplied id): <c>--id</c> when given, else the body's own
    /// <c>id</c>, else a new one. Deciding it up front, rather than reading the <c>Location</c>
    /// header afterwards, is also what makes <c>--id</c> work with a body at all.
    /// </summary>
    /// <param name="body">The parsed body; its <c>id</c> is set in place.</param>
    /// <param name="id">The <c>--id</c> value, or null.</param>
    /// <param name="create">The raw create call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created item's id, name and alias, or the create's failure.</returns>
    /// <exception cref="InvalidOperationException">
    /// The body is not a JSON object, its <c>id</c> is not a UUID, or it differs from <c>--id</c>.
    /// </exception>
    public static async Task<UmbracoResponse<RawCreated>> CreateAsync(
        JsonNode body,
        Guid? id,
        Func<JsonNode, CancellationToken, Task<UmbracoResponse<Empty>>> create,
        CancellationToken ct
    )
    {
        if (body is not JsonObject obj)
            throw new InvalidInputException(
                "--json-body did not contain a JSON object. Run with --example to print a real one."
            );

        var bodyId = obj["id"] switch
        {
            null => (Guid?)null,
            JsonValue v when v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) => g,
            var other => throw new InvalidInputException(
                $"The --json-body id {other.ToJsonString()} is not a UUID."
            ),
        };
        var resolved = ReconcileId(id, bodyId) ?? Guid.NewGuid();
        obj["id"] = resolved.ToString();

        var created = await create(obj, ct);
        return created.Map(_ => new RawCreated(resolved, Text(obj, "name"), Text(obj, "alias")));
    }

    /// <summary>
    /// The id a create should use given <c>--id</c> and the body's own: either one, or neither.
    /// Two different ids is a contradiction; picking either would create the item somewhere the
    /// caller did not expect. Shared with <c>content create</c> (#241).
    /// </summary>
    /// <param name="flag">The <c>--id</c> value.</param>
    /// <param name="fromBody">The body's id.</param>
    /// <returns>The id to use, or null when neither was given.</returns>
    /// <exception cref="InvalidOperationException">The two are both given and differ.</exception>
    internal static Guid? ReconcileId(Guid? flag, Guid? fromBody) =>
        flag is { } f && fromBody is { } b && f != b
            ? throw new InvalidInputException(
                $"--id {f} does not match the id {b} in --json-body. Give one, or make them agree."
            )
            : flag ?? fromBody;

    private static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Joins option names for a message: <c>--a</c>, <c>--a and --b</c>, <c>--a, --b and --c</c>.</summary>
    /// <param name="names">The names.</param>
    /// <returns>The joined text.</returns>
    private static string Join(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count <= 1
            ? string.Concat(list)
            : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }
}
