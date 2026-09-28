using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// A schema snapshot written by hand (#198): a section left out is not managed, references may
/// name their target, and new entities, properties and containers need no id. Driven through the
/// real parser, <see cref="SchemaReferences"/>, the diff engine and the pipeline over the fake client.
/// </summary>
public class SchemaHandAuthoringTests
{
    private static readonly Guid TextstringId = Guid.NewGuid();
    private static readonly Guid BlogId = Guid.NewGuid();
    private static readonly Guid ContentTabId = Guid.NewGuid();
    private static readonly Guid TitleId = Guid.NewGuid();

    /// <summary>The live blog post type: a Content tab holding a title property.</summary>
    private static JsonObject LiveBlog() =>
        (JsonObject)
            JsonNode.Parse(
                $$"""
                {"id":"{{BlogId}}","alias":"blogPost","name":"Blog Post",
                 "containers":[{"id":"{{ContentTabId}}","parent":null,"name":"Content","type":"Tab","sortOrder":0}],
                 "properties":[{"id":"{{TitleId}}","alias":"title","name":"Title",
                   "container":{"id":"{{ContentTabId}}"},"dataType":{"id":"{{TextstringId}}" } }]}
                """
            )!;

    private static SchemaSnapshot Live(params JsonNode[] documentTypes) =>
        new() { DocumentTypes = [.. documentTypes] };

    private static Task<UmbracoResponse<Empty>> Normalise(
        SchemaSnapshot desired,
        SchemaSnapshot live,
        FakeUmbracoManagementClient? fake = null
    ) =>
        SchemaReferences.NormaliseAsync(
            desired,
            live,
            fake ?? new FakeUmbracoManagementClient(),
            CancellationToken.None
        );

    private static JsonObject Only(SchemaSnapshot snapshot) =>
        (JsonObject)snapshot.DocumentTypes!.Single();

    private static string? IdAt(JsonNode? node) => (string?)node?["id"];

    // ── partial snapshots ──────────────────────────────────────────────────────

    [Fact]
    public void FromJson_SectionLeftOut_IsAbsent()
    {
        // Act
        var snapshot = SchemaSnapshot.FromJson("""{"schemaVersion":"4","documentTypes":[]}""");

        // Assert
        Assert.Equal((false, true), (snapshot.DocumentTypes is null, snapshot.DataTypes is null));
    }

    [Fact]
    public void FromJson_SectionSetToNull_IsAbsent()
    {
        // Act
        var snapshot = SchemaSnapshot.FromJson("""{"schemaVersion":"4","templates":null}""");

        // Assert
        Assert.Null(snapshot.Templates);
    }

    [Fact]
    public void Compare_SectionLeftOut_IsNeitherDiffedNorPruned()
    {
        // Arrange: the file manages document types only; the instance has a data type.
        var desired = SchemaSnapshot.FromJson("""{"schemaVersion":"4","documentTypes":[]}""");
        var live = new SchemaSnapshot
        {
            DataTypes = [JsonNode.Parse($$"""{"id":"{{TextstringId}}","name":"Textstring"}""")!],
        };

        // Act
        var diff = SchemaDiffEngine.Compare(desired, live);

        // Assert
        Assert.Same(SchemaKindDiff.None, diff.DataTypes);
    }

    [Fact]
    public async Task ExportAsync_OnlyTheGivenKinds_LeavesTheRestAbsent()
    {
        // Act
        var result = await SchemaExporter.ExportAsync(
            new FakeUmbracoManagementClient(),
            [SchemaKinds.Of(SchemaKinds.DocumentType)],
            CancellationToken.None
        );

        // Assert
        Assert.Equal(
            (false, true, true),
            (
                result.Data!.DocumentTypes is null,
                result.Data.DataTypes is null,
                result.Data.PartialViews is null
            )
        );
    }

    // ── references by name ─────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_DataTypeByName_ResolvesOnTheInstance()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DataType, "Textstring")] = TextstringId;
        var body = LiveBlog();
        body["properties"]![0]!["dataType"] = "Textstring";
        var desired = Live(body);

        // Act
        await Normalise(desired, Live(LiveBlog()), fake);

        // Assert
        Assert.Equal(TextstringId.ToString(), IdAt(Only(desired)["properties"]![0]!["dataType"]));
    }

    [Fact]
    public async Task NormaliseAsync_DataTypeTheSnapshotCreates_ResolvesToItsNewId()
    {
        // Arrange: a new data type with no id, used by name from a new document type.
        var fake = new FakeUmbracoManagementClient();
        var desired = new SchemaSnapshot
        {
            DataTypes = [JsonNode.Parse("""{"name":"Hero Picker","editorAlias":"x"}""")!],
            DocumentTypes =
            [
                JsonNode.Parse(
                    """{"alias":"hero","properties":[{"alias":"image","dataType":{"id":"hero picker"}}]}"""
                )!,
            ],
        };

        // Act
        await Normalise(desired, new SchemaSnapshot(), fake);

        // Assert
        Assert.Equal(
            (string?)desired.DataTypes![0]["id"],
            IdAt(Only(desired)["properties"]![0]!["dataType"])
        );
    }

    [Fact]
    public async Task NormaliseAsync_UnknownName_IsRefusedSayingWhere()
    {
        // Arrange
        var body = LiveBlog();
        body["properties"]![0]!["dataType"] = "Nope";

        // Act
        var error = await Assert.ThrowsAsync<InvalidInputException>(() =>
            Normalise(Live(body), Live(LiveBlog()))
        );

        // Assert
        Assert.Contains("documentType 'blogPost': properties[title].dataType", error.Message);
    }

    [Fact]
    public async Task NormaliseAsync_NameTwoSnapshotEntriesShare_IsRefused()
    {
        // Arrange
        var desired = new SchemaSnapshot
        {
            Templates =
            [
                JsonNode.Parse($$"""{"id":"{{Guid.NewGuid()}}","alias":"a","name":"Base"}""")!,
                JsonNode.Parse($$"""{"id":"{{Guid.NewGuid()}}","alias":"b","name":"Base"}""")!,
                JsonNode.Parse("""{"alias":"page","masterTemplate":"Base"}""")!,
            ],
        };

        // Act + Assert
        await Assert.ThrowsAsync<InvalidInputException>(() =>
            Normalise(desired, new SchemaSnapshot())
        );
    }

    [Fact]
    public async Task NormaliseAsync_BareIdString_IsWrappedAsAReference()
    {
        // Arrange
        var desired = new SchemaSnapshot
        {
            Templates = [JsonNode.Parse($$"""{"alias":"page","masterTemplate":"{{BlogId}}"}""")!],
        };

        // Act
        await Normalise(desired, new SchemaSnapshot());

        // Assert
        Assert.Equal(BlogId.ToString(), IdAt(desired.Templates![0]["masterTemplate"]));
    }

    [Theory]
    [InlineData("\"blogPost\"")] // a bare name
    [InlineData("{\"id\":\"blogPost\"}")] // a reference object in place of the item
    public async Task NormaliseAsync_BareNameInAllowedDocumentTypes_BecomesASortedElement(
        string item
    )
    {
        // Arrange: #357 - the bare name passed the dry run, then the API refused the shape
        // halfway through the apply.
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DocumentType, "blogPost")] = BlogId;
        var desired = Live(
            JsonNode.Parse($$"""{"alias":"blog","allowedDocumentTypes":[{{item}}]}""")!
        );

        // Act
        await Normalise(desired, new SchemaSnapshot(), fake);

        // Assert
        Assert.Equal(
            $$"""[{"documentType":{"id":"{{BlogId}}"},"sortOrder":0}]""",
            Only(desired)["allowedDocumentTypes"]!.ToJsonString()
        );
    }

    [Fact]
    public async Task NormaliseAsync_BareNameInCompositions_BecomesACompositionElement()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DocumentType, "blogPost")] = BlogId;
        var desired = Live(JsonNode.Parse("""{"alias":"page","compositions":["blogPost"]}""")!);

        // Act
        await Normalise(desired, new SchemaSnapshot(), fake);

        // Assert
        Assert.Equal(
            $$"""[{"documentType":{"id":"{{BlogId}}"},"compositionType":"Composition"}]""",
            Only(desired)["compositions"]!.ToJsonString()
        );
    }

    [Fact]
    public async Task NormaliseAsync_BareReferenceWhereAnObjectIsNeeded_IsRefusedSayingWhere()
    {
        // Arrange: a permission carries verbs, so it cannot be built from a type name alone.
        var desired = new SchemaSnapshot
        {
            UserGroups =
            [
                JsonNode.Parse(
                    """{"alias":"editors","name":"Editors","permissions":["blogPost"]}"""
                )!,
            ],
        };

        // Act
        var error = await Assert.ThrowsAsync<InvalidInputException>(() =>
            Normalise(desired, new SchemaSnapshot())
        );

        // Assert
        Assert.Contains(
            "permissions[0] must be an object with a 'documentType' field",
            error.Message
        );
    }

    /// <summary>
    /// A snapshot with a new type whose display name is <c>permPage</c>, and a type that allows
    /// <c>permPage</c> as a child (#359).
    /// </summary>
    private static (SchemaSnapshot Desired, Guid ChildId) NameShadowSnapshot()
    {
        var childId = Guid.NewGuid();
        var desired = Live(
            JsonNode.Parse($$"""{"id":"{{childId}}","alias":"child","name":"permPage"}""")!,
            JsonNode.Parse(
                """{"alias":"parent","allowedDocumentTypes":[{"documentType":"permPage","sortOrder":0}]}"""
            )!
        );
        return (desired, childId);
    }

    [Fact]
    public async Task NormaliseAsync_SnapshotNameThatIsALiveAlias_IsRefusedAsAmbiguous()
    {
        // Arrange: the instance has another type whose alias is permPage.
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DocumentType, "permPage")] = Guid.NewGuid();
        var (desired, _) = NameShadowSnapshot();

        // Act
        var error = await Assert.ThrowsAsync<InvalidInputException>(() =>
            Normalise(desired, new SchemaSnapshot(), fake)
        );

        // Assert
        Assert.Contains("ambiguous", error.Message);
    }

    [Fact]
    public async Task NormaliseAsync_SnapshotNameTheInstanceDoesNotHave_ResolvesToTheSnapshotEntry()
    {
        // Arrange
        var (desired, childId) = NameShadowSnapshot();

        // Act
        await Normalise(desired, new SchemaSnapshot());

        // Assert
        Assert.Equal(
            childId.ToString(),
            IdAt(desired.DocumentTypes![1]["allowedDocumentTypes"]![0]!["documentType"])
        );
    }

    [Fact]
    public async Task NormaliseAsync_ExportedSnapshot_IsLeftExactlyAsItIs()
    {
        // Arrange
        var desired = Live(LiveBlog());
        var before = desired.ToJson();

        // Act
        await Normalise(desired, Live(LiveBlog()));

        // Assert
        Assert.Equal(before, desired.ToJson());
    }

    // ── ids for new entries ────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_EntityWithoutId_TakesTheLiveIdOfItsAlias()
    {
        // Arrange
        var body = LiveBlog();
        body.Remove("id");
        var desired = Live(body);

        // Act
        await Normalise(desired, Live(LiveBlog()));

        // Assert
        Assert.Equal(BlogId.ToString(), (string?)Only(desired)["id"]);
    }

    [Fact]
    public async Task NormaliseAsync_ExistingPropertyWithoutId_KeepsItsLiveId()
    {
        // Arrange: the author left the ids out; the title property must keep its values.
        var body = LiveBlog();
        body["properties"]![0]!.AsObject().Remove("id");
        var desired = Live(body);

        // Act
        await Normalise(desired, Live(LiveBlog()));

        // Assert
        Assert.Equal(TitleId.ToString(), (string?)Only(desired)["properties"]![0]!["id"]);
    }

    [Fact]
    public async Task NormaliseAsync_NewPropertyWithoutId_GetsANewId()
    {
        // Arrange
        var body = LiveBlog();
        body["properties"]!
            .AsArray()
            .Add(
                JsonNode.Parse(
                    $$"""{"alias":"summary","container":"Content","dataType":{"id":"{{TextstringId}}" } }"""
                )
            );
        var desired = Live(body);

        // Act
        await Normalise(desired, Live(LiveBlog()));

        // Assert
        var id = (string?)Only(desired)["properties"]![1]!["id"];
        Assert.True(Guid.TryParse(id, out var parsed) && parsed != TitleId);
    }

    [Fact]
    public async Task Compare_HandWrittenTypeMatchingLive_IsUnchanged()
    {
        // Arrange: the live type written by hand without any ids, by name.
        var body = (JsonObject)
            JsonNode.Parse(
                """
                {"alias":"blogPost","name":"Blog Post",
                 "containers":[{"parent":null,"name":"Content","type":"Tab","sortOrder":0}],
                 "properties":[{"alias":"title","name":"Title","container":"Content","dataType":"Textstring"}]}
                """
            )!;
        var fake = new FakeUmbracoManagementClient();
        fake.References[(EntityKind.DataType, "Textstring")] = TextstringId;
        var desired = Live(body);
        var live = Live(LiveBlog());
        await Normalise(desired, live, fake);

        // Act
        var diff = SchemaDiffEngine.Compare(desired, live);

        // Assert
        Assert.Equal(1, diff.DocumentTypes.Unchanged);
    }

    // ── containers by name ─────────────────────────────────────────────────────

    [Fact]
    public async Task NormaliseAsync_ContainerWithoutId_TakesTheLiveContainersId()
    {
        // Arrange
        var body = LiveBlog();
        body["containers"]![0]!.AsObject().Remove("id");
        body["properties"]![0]!["container"] = "Content";
        var desired = Live(body);

        // Act
        await Normalise(desired, Live(LiveBlog()));

        // Assert
        Assert.Equal(ContentTabId.ToString(), IdAt(Only(desired)["properties"]![0]!["container"]));
    }

    [Fact]
    public async Task NormaliseAsync_GroupNameUnderTwoTabs_NeedsThePath()
    {
        // Arrange: a Hero group in each of two tabs; "Hero" alone names both.
        var body = (JsonObject)
            JsonNode.Parse(
                """
                {"alias":"page","containers":[
                   {"name":"Content","type":"Tab"},
                   {"name":"Settings","type":"Tab"},
                   {"name":"Hero","type":"Group","parent":"Content"},
                   {"name":"Hero","type":"Group","parent":"Settings"}],
                 "properties":[{"alias":"title","container":"Hero"}]}
                """
            )!;

        // Act
        var error = await Assert.ThrowsAsync<InvalidInputException>(() =>
            Normalise(Live(body), new SchemaSnapshot())
        );

        // Assert
        Assert.Contains("'Tab/Group' path", error.Message);
    }

    [Fact]
    public async Task NormaliseAsync_ContainerPath_PicksTheGroup()
    {
        // Arrange
        var body = (JsonObject)
            JsonNode.Parse(
                """
                {"alias":"page","containers":[
                   {"name":"Content","type":"Tab"},
                   {"name":"Content","type":"Group","parent":{"id":"Content"}}],
                 "properties":[{"alias":"title","container":"content/content"}]}
                """
            )!;
        var desired = Live(body);

        // Act
        await Normalise(desired, new SchemaSnapshot());

        // Assert
        Assert.Equal(
            (string?)Only(desired)["containers"]![1]!["id"],
            IdAt(Only(desired)["properties"]![0]!["container"])
        );
    }

    // ── pipeline ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiffAgainstLiveAsync_PartialFile_DoesNotReadOrPruneOtherKinds()
    {
        // Arrange: the instance has a template; the file manages document types only.
        var fake = new FakeUmbracoManagementClient();
        var template = Guid.NewGuid();
        fake.TemplateList.Add(new TemplateResponse { Id = template, Alias = "home" });
        fake.TemplateRaw[template] = JsonNode.Parse($$"""{"id":"{{template}}","alias":"home"}""")!;
        var path = Path.Combine(Path.GetTempPath(), $"partial-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """{"schemaVersion":"4","documentTypes":[{"alias":"blogPost"}]}""");

        // Act
        var diff = await SchemaPipeline.DiffAgainstLiveAsync(fake, path, CancellationToken.None);

        // Assert
        Assert.Equal(
            (true, 1, 0),
            (
                diff.IsSuccess,
                diff.Data!.DocumentTypes.Added.Count,
                diff.Data.Templates.Removed.Count
            )
        );
    }
}
