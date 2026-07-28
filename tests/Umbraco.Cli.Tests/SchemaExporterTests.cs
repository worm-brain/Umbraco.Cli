using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// Behaviour of <see cref="SchemaExporter"/> (#68 / ADR 0004): it enumerates every schema
/// entity and assembles their verbatim bodies into a snapshot, pages past the first page, and
/// fails fast (never returns a partial snapshot) when any read fails.
/// </summary>
public class SchemaExporterTests
{
    private static JsonNode Body(Guid id, string alias) =>
        JsonNode.Parse($$"""{"id":"{{id}}","alias":"{{alias}}","name":"{{alias}}"}""")!;

    [Fact]
    public async Task ExportAsync_AssemblesVerbatimBodiesForAllThreeKinds()
    {
        var fake = new FakeUmbracoManagementClient();
        var docId = Guid.NewGuid();
        var dataId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = docId, Alias = "blogPost" });
        fake.DocumentTypeRaw[docId] = Body(docId, "blogPost");
        fake.DataTypeList.Add(new DataTypeResponse { Id = dataId, Name = "My Slider" });
        fake.DataTypeRaw[dataId] = Body(dataId, "My Slider");
        fake.TemplateList.Add(new TemplateResponse { Id = templateId, Alias = "home" });
        fake.TemplateRaw[templateId] = Body(templateId, "home");

        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snap = result.Data!;
        Assert.Equal(SchemaSnapshot.CurrentVersion, snap.SchemaVersion);
        Assert.Equal("blogPost", (string?)snap.DocumentTypes.Single()["alias"]);
        Assert.Equal("My Slider", (string?)snap.DataTypes.Single()["alias"]);
        Assert.Equal("home", (string?)snap.Templates.Single()["alias"]);
    }

    [Fact]
    public async Task ExportAsync_FailsFast_WhenAPerEntityReadFails()
    {
        // A doc type appears in the list but its raw body 404s: the export must fail rather
        // than return a snapshot that looks like the entity was deleted.
        var fake = new FakeUmbracoManagementClient();
        var docId = Guid.NewGuid();
        fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = docId, Alias = "orphan" });
        // Deliberately no fake.DocumentTypeRaw entry -> Raw() returns 404.

        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ExportAsync_ReadsABodyForEveryEnumeratedId()
    {
        // The client enumerates all ids (tree-walk/paging lives there); the exporter must read a
        // full body for every one of them. 150 proves it does not stop early.
        var fake = new FakeUmbracoManagementClient();
        for (var i = 0; i < 150; i++)
        {
            var id = Guid.NewGuid();
            fake.DocumentTypeList.Add(new DocumentTypeResponse { Id = id, Alias = $"dt{i}" });
            fake.DocumentTypeRaw[id] = Body(id, $"dt{i}");
        }

        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150, result.Data!.DocumentTypes.Count);
    }

    [Fact]
    public async Task ExportAsync_EmptyInstance_ProducesEmptySnapshot()
    {
        var fake = new FakeUmbracoManagementClient();

        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.DocumentTypes);
        Assert.Empty(result.Data!.DataTypes);
        Assert.Empty(result.Data!.Templates);
    }
}
