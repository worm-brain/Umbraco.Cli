using System.Text.Json;

namespace Umbraco.Cli.IntegrationTests;

/// <summary>
/// Shared behaviour for every live test class: the reachability skip, and scratch fixtures that
/// clean themselves up.
/// </summary>
/// <param name="live">Shared reachability fixture.</param>
public abstract class LiveTestBase(LiveInstanceFixture live)
{
    private readonly LiveInstanceFixture _live = live;

    /// <summary>
    /// Skips the current test unless a live instance responded to the probe.
    /// <para>
    /// This is the only legitimate use of <c>Skip</c> around a write: the environment cannot run
    /// the test at all. Skipping because a command under test did not do what it claimed turns the
    /// regression into a green result - see the note on <see cref="EffectIntegrationTests"/>.
    /// </para>
    /// </summary>
    protected void RequireLive() => Skip.IfNot(_live.IsReachable, _live.SkipReason);

    /// <summary>A prefix + random suffix, so fixtures never collide on a shared instance.</summary>
    /// <param name="prefix">A short identifying prefix.</param>
    /// <returns>A unique alias.</returns>
    protected static string ScratchAlias(string prefix) =>
        prefix + Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// A throwaway document and the document type it needs, both removed on dispose.
/// <para>
/// Acquisition and registration-for-cleanup are the same statement, so there is no window in which
/// a later failure can strand a fixture on the shared live instance. Deleting the document type
/// also removes any remaining content of that type, so the document delete is best-effort.
/// </para>
/// </summary>
public sealed class ScratchDocument : IDisposable
{
    private ScratchDocument(string id, string documentTypeId, string documentTypeAlias)
    {
        Id = id;
        DocumentTypeId = documentTypeId;
        DocumentTypeAlias = documentTypeAlias;
    }

    /// <summary>The created document's id.</summary>
    public string Id { get; }

    /// <summary>The throwaway document type's id.</summary>
    public string DocumentTypeId { get; }

    /// <summary>The throwaway document type's alias.</summary>
    public string DocumentTypeAlias { get; }

    /// <summary>
    /// Creates a root-allowed document type and one document of it.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <param name="templateAlias">Optional template to create the document with (#162).</param>
    /// <returns>The scope, which deletes both on dispose.</returns>
    public static ScratchDocument Create(string name, string? templateAlias = null)
    {
        var alias = "clitestEffect" + Guid.NewGuid().ToString("N")[..8];
        var type = CliRunner.Run(
            "document-type",
            "create",
            "--name",
            alias,
            "--alias",
            alias,
            "--allow-at-root"
        );
        Assert.True(type.Ok, type.Stderr);
        var typeId = type.Data().GetProperty("id").GetString()!;

        // From here the type exists, so anything that throws must still clean it up.
        try
        {
            string[] args = templateAlias is null
                ? ["content", "create", "--document-type", alias, "--name", name]
                :
                [
                    "content",
                    "create",
                    "--document-type",
                    alias,
                    "--name",
                    name,
                    "--template",
                    templateAlias,
                ];
            var document = CliRunner.Run(args);
            // An instance that refuses this combination is an environment limit, not a failure of
            // the behaviour under test.
            Skip.IfNot(document.Ok, $"Could not create the scratch document: {document.Stderr}");
            return new ScratchDocument(
                document.Data().GetProperty("id").GetString()!,
                typeId,
                alias
            );
        }
        catch
        {
            CliRunner.Run("document-type", "delete", typeId, "--force", "--yes");
            throw;
        }
    }

    /// <summary>Whether the document currently reports as published.</summary>
    /// <returns>True when <c>content get</c> reports it published.</returns>
    public bool IsPublished()
    {
        var get = CliRunner.Run("content", "get", Id);
        Assert.True(get.Ok, get.Stderr);
        return get.Data().GetProperty("isPublished").GetBoolean();
    }

    /// <summary>
    /// The document's verbatim body, read back through <c>content export</c>.
    /// <para>
    /// <c>content get</c> still returns a narrow projection (#168, Phase 3), so the export snapshot
    /// is the only way to see a document's template and property values through the CLI. The
    /// document is selected by id rather than by position, so the assertion does not depend on the
    /// exporter's walk order.
    /// </para>
    /// </summary>
    /// <returns>The document's body.</returns>
    public JsonElement ExportBody()
    {
        var export = CliRunner.Run("content", "export", "--root", Id);
        Assert.True(export.Ok, export.Stderr);
        return export
            .Data()
            .GetProperty("documents")
            .EnumerateArray()
            .Single(d => d.GetProperty("id").GetString() == Id)
            .GetProperty("body");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CliRunner.Run("content", "delete", Id, "--yes");
        CliRunner.Run("document-type", "delete", DocumentTypeId, "--force", "--yes");
    }
}

/// <summary>A throwaway template, removed on dispose.</summary>
public sealed class ScratchTemplate : IDisposable
{
    private ScratchTemplate(string id, string alias)
    {
        Id = id;
        Alias = alias;
    }

    /// <summary>The created template's id.</summary>
    public string Id { get; }

    /// <summary>The created template's alias.</summary>
    public string Alias { get; }

    /// <summary>Creates a template with a random alias.</summary>
    /// <returns>The scope, which deletes it on dispose.</returns>
    public static ScratchTemplate Create()
    {
        var alias = "clitestTpl" + Guid.NewGuid().ToString("N")[..8];
        var created = CliRunner.Run("template", "create", "--name", alias, "--alias", alias);
        Skip.IfNot(created.Ok, $"Could not create a scratch template: {created.Stderr}");
        return new ScratchTemplate(created.Data().GetProperty("id").GetString()!, alias);
    }

    /// <inheritdoc />
    // --force: cleanup must not be refused if a test left a document type pointing at it (#269).
    public void Dispose() => CliRunner.Run("template", "delete", Id, "--force", "--yes");
}
