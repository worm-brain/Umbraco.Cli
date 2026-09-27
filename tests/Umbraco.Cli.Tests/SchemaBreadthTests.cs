using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The schema snapshot's newer kinds (#227): languages (keyed by ISO code, no id), dictionary
/// items (hierarchical), member groups and user groups (with their instance-specific parts left
/// out). Covers export shaping, the diff's matching and undeletable rules, apply order, the
/// kind-specific updates, and the prune guards. Driven through the fake client.
/// </summary>
public class SchemaBreadthTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static JsonNode Language(string iso, bool isDefault = false, string? fallback = null) =>
        new JsonObject
        {
            ["isoCode"] = iso,
            ["name"] = iso,
            ["isDefault"] = isDefault,
            ["isMandatory"] = false,
            ["fallbackIsoCode"] = fallback,
        };

    private static JsonNode Dictionary(Guid id, string name, Guid? parent = null) =>
        SchemaBodies.DictionaryItem(
            new JsonObject
            {
                ["id"] = id.ToString(),
                ["name"] = name,
                ["translations"] = new JsonArray(),
            },
            parent
        );

    private static JsonNode UserGroup(Guid id, string alias, bool deletable = true) =>
        new JsonObject
        {
            ["id"] = id.ToString(),
            ["alias"] = alias,
            ["name"] = alias,
            ["isDeletable"] = deletable,
            ["sections"] = new JsonArray("Umb.Section.Content"),
            ["permissions"] = new JsonArray(),
        };

    private static SchemaEntityChange Added(
        string kind,
        string identity,
        Guid? id,
        JsonNode body
    ) => new(kind, SchemaChangeKind.Added, identity, id, null) { DesiredBody = body };

    private static SchemaEntityChange Removed(
        string kind,
        string identity,
        Guid? id,
        JsonNode body
    ) => new(kind, SchemaChangeKind.Removed, identity, null, id) { CurrentBody = body };

    private static SchemaKindDiff Adds(params SchemaEntityChange[] added) =>
        new(added, [], [], [], 0);

    private static SchemaKindDiff Removes(params SchemaEntityChange[] removed) =>
        new([], [], removed, [], 0);

    private static SchemaDiff Diff() =>
        new(
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None,
            SchemaKindDiff.None
        );

    private static Task<UmbracoResponse<SchemaApplyResult>> Apply(
        FakeUmbracoManagementClient fake,
        SchemaDiff diff,
        bool prune = false,
        bool dryRun = false,
        bool force = false
    ) =>
        SchemaApplier.ApplyAsync(
            fake,
            diff,
            new SchemaApplyOptions(prune, dryRun, force),
            CancellationToken.None
        );

    // ── export ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportAsync_CarriesLanguagesAndMemberGroups()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        fake.LanguagesRaw.Add(Language("en-US", isDefault: true));
        var group = Guid.NewGuid();
        fake.MemberGroupIds.Add(group);
        fake.MemberGroupRaw[group] = JsonNode.Parse($$"""{"id":"{{group}}","name":"VIP"}""")!;

        // Act
        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Multiple(
            () => Assert.Equal("en-US", (string?)result.Data!.Languages.Single()["isoCode"]),
            () => Assert.Equal("VIP", (string?)result.Data!.MemberGroups.Single()["name"])
        );
    }

    [Fact]
    public async Task ExportAsync_DictionaryItem_GetsItsParentAndSortedTranslations()
    {
        // Arrange: the item read has no parent and returns translations unordered.
        var fake = new FakeUmbracoManagementClient();
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        fake.DictionaryEntries.Add(new DictionaryEntry(child, parent));
        fake.DictionaryItemRaw[child] = JsonNode.Parse(
            $$"""
            {"id":"{{child}}","name":"Blog.Title","translations":[
              {"isoCode":"en-US","translation":"Title"},
              {"isoCode":"da-DK","translation":"Titel"}]}
            """
        )!;

        // Act
        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        // Assert
        var item = result.Data!.DictionaryItems.Single();
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(
                    $$"""
                    {"id":"{{child}}","name":"Blog.Title","parent":{"id":"{{parent}}"},"translations":[
                      {"isoCode":"da-DK","translation":"Titel"},
                      {"isoCode":"en-US","translation":"Title"}]}
                    """
                ),
                item
            ),
            item.ToJsonString()
        );
    }

    [Fact]
    public async Task ExportAsync_UserGroup_LeavesOutStartNodesAndPerDocumentPermissions()
    {
        // Arrange
        var fake = new FakeUmbracoManagementClient();
        var id = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var docType = Guid.NewGuid();
        fake.UserGroupIds.Add(id);
        fake.UserGroupRaw[id] = JsonNode.Parse(
            $$"""
            {"id":"{{id}}","alias":"editor","documentStartNode":{"id":"{{doc}}"},"mediaStartNode":null,
             "documentRootAccess":false,"permissions":[
               {"$type":"DocumentPermissionPresentationModel","document":{"id":"{{doc}}"},"verbs":["Umb.Document.Read"]},
               {"$type":"DocumentPropertyValuePermissionPresentationModel","documentType":{"id":"{{docType}}"},"verbs":[]}]}
            """
        )!;

        // Act
        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        // Assert
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(
                    $$"""
                    {"id":"{{id}}","alias":"editor","documentRootAccess":false,"permissions":[
                      {"$type":"DocumentPropertyValuePermissionPresentationModel","documentType":{"id":"{{docType}}"},"verbs":[]}]}
                    """
                ),
                result.Data!.UserGroups.Single()
            ),
            result.Data!.UserGroups.Single().ToJsonString()
        );
    }

    [Fact]
    public async Task ExportAsync_FailsFast_WhenADictionaryItemReadFails()
    {
        // The item is enumerated but its body is missing: a partial snapshot would apply as if
        // the item had been deleted.
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryEntries.Add(new DictionaryEntry(Guid.NewGuid(), null));

        var result = await SchemaExporter.ExportAsync(fake, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void FromJson_Version2Snapshot_IsRefusedRatherThanReadAsEmpty()
    {
        // A version-2 file has no languages, dictionary or groups: read as empty, --prune would
        // delete all of them.
        var v2 = """{"schemaVersion":"2","documentTypes":[],"dataTypes":[],"templates":[]}""";

        Assert.Throws<JsonException>(() => SchemaSnapshot.FromJson(v2));
    }

    // ── diff ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_LanguageMatchedByIsoCode_IsNotAPruneCandidate()
    {
        // Languages have no id. Matching them by ISO code must also mark the live one matched;
        // otherwise every language would be listed for deletion.
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot { Languages = [Language("da-DK")] },
            new SchemaSnapshot { Languages = [Language("da-DK")] }
        );

        Assert.Multiple(
            () => Assert.Empty(diff.Languages.Removed),
            () => Assert.Equal(1, diff.Languages.Unchanged)
        );
    }

    [Fact]
    public void Compare_LanguageSettingsDiffer_IsChanged()
    {
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot { Languages = [Language("da-DK", fallback: "en-US")] },
            new SchemaSnapshot { Languages = [Language("da-DK")] }
        );

        var changed = Assert.Single(diff.Languages.Changed);
        Assert.Equal(["fallbackIsoCode"], changed.Changes);
    }

    [Fact]
    public void Compare_UnmatchedDefaultLanguage_IsSkippedNotRemoved()
    {
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot(),
            new SchemaSnapshot { Languages = [Language("en-US", isDefault: true)] }
        );

        Assert.Multiple(
            () => Assert.Empty(diff.Languages.Removed),
            () => Assert.Contains("default language", Assert.Single(diff.Languages.Skipped).Note)
        );
    }

    [Fact]
    public void Compare_UnmatchedUndeletableUserGroup_IsSkippedNotRemoved()
    {
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot(),
            new SchemaSnapshot
            {
                UserGroups = [UserGroup(Guid.NewGuid(), "admin", deletable: false)],
            }
        );

        Assert.Multiple(
            () => Assert.Empty(diff.UserGroups.Removed),
            () => Assert.Single(diff.UserGroups.Skipped)
        );
    }

    [Fact]
    public void Compare_UnmatchedDeletableUserGroup_IsRemoved()
    {
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot(),
            new SchemaSnapshot { UserGroups = [UserGroup(Guid.NewGuid(), "writers")] }
        );

        Assert.Equal("writers", Assert.Single(diff.UserGroups.Removed).Identity);
    }

    // ── apply: order and writes ────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_CreatesLanguagesThenDictionaryThenGroups_ReferencedFirst()
    {
        // Arrange: listed referrer-first within each kind, to prove the sort.
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var group = Guid.NewGuid();
        var users = Guid.NewGuid();
        var diff = Diff() with
        {
            UserGroups = Adds(
                Added(SchemaKinds.UserGroup, "writers", users, UserGroup(users, "writers"))
            ),
            MemberGroups = Adds(
                Added(
                    SchemaKinds.MemberGroup,
                    "VIP",
                    group,
                    new JsonObject { ["id"] = group.ToString() }
                )
            ),
            DictionaryItems = Adds(
                Added(
                    SchemaKinds.DictionaryItem,
                    "Blog.Title",
                    child,
                    Dictionary(child, "Blog.Title", parent)
                ),
                Added(SchemaKinds.DictionaryItem, "Blog", parent, Dictionary(parent, "Blog"))
            ),
            Languages = Adds(
                Added(SchemaKinds.Language, "da-DK", null, Language("da-DK", fallback: "en-GB")),
                Added(SchemaKinds.Language, "en-GB", null, Language("en-GB"))
            ),
        };
        var fake = new FakeUmbracoManagementClient();

        // Act
        var result = await Apply(fake, diff);

        // Assert
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            ["en-GB", "da-DK", "Blog", "Blog.Title", "VIP", "writers"],
            result.Data!.Actions.Select(a => a.Identity)
        );
    }

    [Fact]
    public async Task ApplyAsync_LanguageUpdate_WritesByIsoCode()
    {
        var desired = Language("da-DK", fallback: "en-US");
        var diff = Diff() with
        {
            Languages = new SchemaKindDiff(
                [],
                [
                    new(SchemaKinds.Language, SchemaChangeKind.Changed, "da-DK", null, null)
                    {
                        DesiredBody = desired,
                    },
                ],
                [],
                [],
                0
            ),
        };
        var fake = new FakeUmbracoManagementClient();

        await Apply(fake, diff);

        Assert.Equal("da-DK", Assert.Single(fake.LanguageUpdates).IsoCode);
    }

    [Fact]
    public async Task ApplyAsync_DictionaryUpdateWithNewParent_UpdatesWithoutParentThenMoves()
    {
        // Arrange
        var id = Guid.NewGuid();
        var newParent = Guid.NewGuid();
        var diff = Diff() with
        {
            DictionaryItems = new SchemaKindDiff(
                [],
                [
                    new(SchemaKinds.DictionaryItem, SchemaChangeKind.Changed, "Title", id, id)
                    {
                        DesiredBody = Dictionary(id, "Title", newParent),
                        CurrentBody = Dictionary(id, "Title"),
                    },
                ],
                [],
                [],
                0
            ),
        };
        var fake = new FakeUmbracoManagementClient();

        // Act
        await Apply(fake, diff);

        // Assert
        Assert.Multiple(
            () => Assert.Null(fake.LastSchemaMerge!.Value.Body["parent"]),
            () => Assert.Equal((id, (Guid?)newParent), Assert.Single(fake.DictionaryItemsMoved))
        );
    }

    [Fact]
    public async Task ApplyAsync_DictionaryUpdateSameParent_DoesNotMove()
    {
        var id = Guid.NewGuid();
        var parent = Guid.NewGuid();
        var diff = Diff() with
        {
            DictionaryItems = new SchemaKindDiff(
                [],
                [
                    new(SchemaKinds.DictionaryItem, SchemaChangeKind.Changed, "Title", id, id)
                    {
                        DesiredBody = Dictionary(id, "Title", parent),
                        CurrentBody = Dictionary(id, "Title", parent),
                    },
                ],
                [],
                [],
                0
            ),
        };
        var fake = new FakeUmbracoManagementClient();

        await Apply(fake, diff);

        Assert.Empty(fake.DictionaryItemsMoved);
    }

    [Fact]
    public async Task ApplyAsync_UserGroupUpdate_KeepsTheTargetsStartNodesAndDocumentPermissions()
    {
        // Arrange: the target has its own start node and a per-document permission.
        var id = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.UserGroupRaw[id] = JsonNode.Parse(
            $$"""
            {"id":"{{id}}","alias":"editor","sections":[],"documentStartNode":{"id":"{{doc}}"},
             "permissions":[{"$type":"DocumentPermissionPresentationModel","document":{"id":"{{doc}}"},"verbs":[]}]}
            """
        )!;
        var desired = UserGroup(id, "editor");
        var diff = Diff() with
        {
            UserGroups = new SchemaKindDiff(
                [],
                [
                    new(SchemaKinds.UserGroup, SchemaChangeKind.Changed, "editor", id, id)
                    {
                        DesiredBody = desired,
                    },
                ],
                [],
                [],
                0
            ),
        };

        // Act
        await Apply(fake, diff);

        // Assert
        var sent = fake.LastSchemaMerge!.Value.Body;
        Assert.Multiple(
            () => Assert.Equal(doc.ToString(), (string?)sent["documentStartNode"]?["id"]),
            () =>
                Assert.Equal(doc.ToString(), (string?)sent["permissions"]?[0]?["document"]?["id"]),
            () => Assert.Equal("Umb.Section.Content", (string?)sent["sections"]?[0])
        );
    }

    // ── apply: prune ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_PruneLanguageWithoutForce_IsRefusedAndDeletesNothing()
    {
        var diff = Diff() with
        {
            Languages = Removes(Removed(SchemaKinds.Language, "da-DK", null, Language("da-DK"))),
        };
        var fake = new FakeUmbracoManagementClient();

        await Assert.ThrowsAsync<SafetyRefusalException>(() => Apply(fake, diff, prune: true));
        Assert.Empty(fake.LanguagesDeleted);
    }

    [Fact]
    public async Task ApplyAsync_PruneLanguageDryRun_MarksItNeedsForce()
    {
        var diff = Diff() with
        {
            Languages = Removes(Removed(SchemaKinds.Language, "da-DK", null, Language("da-DK"))),
        };

        var result = await Apply(
            new FakeUmbracoManagementClient(),
            diff,
            prune: true,
            dryRun: true
        );

        Assert.Equal("needs --force", Assert.Single(result.Data!.Actions).Status);
    }

    [Fact]
    public async Task ApplyAsync_PruneLanguagesWithForce_DeletesReferrerBeforeFallback()
    {
        var diff = Diff() with
        {
            Languages = Removes(
                Removed(SchemaKinds.Language, "en-GB", null, Language("en-GB")),
                Removed(SchemaKinds.Language, "da-DK", null, Language("da-DK", fallback: "en-GB"))
            ),
        };
        var fake = new FakeUmbracoManagementClient();

        await Apply(fake, diff, prune: true, force: true);

        Assert.Equal(["da-DK", "en-GB"], fake.LanguagesDeleted);
    }

    [Fact]
    public async Task ApplyAsync_PruneDictionarySubtree_DeletesChildrenFirstWithoutForce()
    {
        // Arrange: parent and child both pruned, so the parent's delete loses nothing extra.
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryEntries.Add(new DictionaryEntry(parent, null));
        fake.DictionaryEntries.Add(new DictionaryEntry(child, parent));
        var diff = Diff() with
        {
            DictionaryItems = Removes(
                Removed(SchemaKinds.DictionaryItem, "Blog", parent, Dictionary(parent, "Blog")),
                Removed(
                    SchemaKinds.DictionaryItem,
                    "Blog.Title",
                    child,
                    Dictionary(child, "Blog.Title", parent)
                )
            ),
        };

        // Act
        await Apply(fake, diff, prune: true);

        // Assert
        Assert.Equal([child, parent], fake.DictionaryItemsDeleted);
    }

    [Fact]
    public async Task ApplyAsync_PruneDictionaryItemWithAKeptChild_IsRefused()
    {
        var parent = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryEntries.Add(new DictionaryEntry(parent, null));
        fake.DictionaryEntries.Add(new DictionaryEntry(Guid.NewGuid(), parent));
        var diff = Diff() with
        {
            DictionaryItems = Removes(
                Removed(SchemaKinds.DictionaryItem, "Blog", parent, Dictionary(parent, "Blog"))
            ),
        };

        var refusal = await Assert.ThrowsAsync<SafetyRefusalException>(() =>
            Apply(fake, diff, prune: true)
        );

        Assert.Contains("child item", refusal.Message);
    }

    [Fact]
    public async Task ApplyAsync_PruneGroups_DeletesUserAndMemberGroups()
    {
        var users = Guid.NewGuid();
        var members = Guid.NewGuid();
        var diff = Diff() with
        {
            UserGroups = Removes(
                Removed(SchemaKinds.UserGroup, "writers", users, UserGroup(users, "writers"))
            ),
            MemberGroups = Removes(
                Removed(
                    SchemaKinds.MemberGroup,
                    "VIP",
                    members,
                    new JsonObject { ["id"] = members.ToString() }
                )
            ),
        };
        var fake = new FakeUmbracoManagementClient();

        await Apply(fake, diff, prune: true);

        Assert.Multiple(
            () => Assert.Equal([users], fake.UserGroupsDeleted),
            () => Assert.Equal([members], fake.MemberGroupsDeleted)
        );
    }

    [Fact]
    public async Task ApplyAsync_PruneUserGroupWithUsers_IsRefusedWithoutForce()
    {
        // #269: prune runs the same check as user-group delete, and refuses the whole apply.
        var users = Guid.NewGuid();
        var diff = Diff() with
        {
            UserGroups = Removes(
                Removed(SchemaKinds.UserGroup, "writers", users, UserGroup(users, "writers"))
            ),
        };
        var fake = new FakeUmbracoManagementClient();
        fake.UserCountsByGroup[users] = 4;

        await Assert.ThrowsAsync<SafetyRefusalException>(() => Apply(fake, diff, prune: true));
        Assert.Empty(fake.UserGroupsDeleted);
    }

    // ── dictionary across instances (ids differ, names match) ──────────────────

    [Fact]
    public void Compare_NameMatchedTreeWithOtherIds_IsUnchanged()
    {
        // Arrange: the same Blog > Blog.Title tree, created by hand on each instance.
        var (sourceParent, sourceChild) = (Guid.NewGuid(), Guid.NewGuid());
        var (targetParent, targetChild) = (Guid.NewGuid(), Guid.NewGuid());
        var desired = new SchemaSnapshot
        {
            DictionaryItems =
            [
                Dictionary(sourceParent, "Blog"),
                Dictionary(sourceChild, "Blog.Title", sourceParent),
            ],
        };
        var live = new SchemaSnapshot
        {
            DictionaryItems =
            [
                Dictionary(targetParent, "Blog"),
                Dictionary(targetChild, "Blog.Title", targetParent),
            ],
        };

        // Act
        var diff = SchemaDiffEngine.Compare(desired, live);

        // Assert: the id mismatch alone is not a change, so a re-apply does nothing.
        Assert.Multiple(
            () => Assert.Empty(diff.DictionaryItems.Changed),
            () => Assert.Equal(2, diff.DictionaryItems.Unchanged)
        );
    }

    [Fact]
    public async Task ApplyAsync_NewChildOfANameMatchedParent_IsCreatedUnderTheTargetsParent()
    {
        // Arrange
        var (sourceParent, targetParent, child) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot
            {
                DictionaryItems =
                [
                    Dictionary(sourceParent, "Blog"),
                    Dictionary(child, "Blog.Title", sourceParent),
                ],
            },
            new SchemaSnapshot { DictionaryItems = [Dictionary(targetParent, "Blog")] }
        );
        var fake = new FakeUmbracoManagementClient();

        // Act
        await Apply(fake, diff);

        // Assert
        var created = Assert.Single(fake.RawWrites, w => w.Kind == "dictionaryItem");
        Assert.Equal(targetParent, SchemaBodies.ParentOf(created.Body));
    }

    [Fact]
    public async Task ApplyAsync_PruneParentWhoseChildIsMovedAway_NeedsNoForce()
    {
        // Arrange: live Old > Title; the snapshot keeps Title but under Blog, and drops Old.
        var (old, title, blog) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryEntries.Add(new DictionaryEntry(old, null));
        fake.DictionaryEntries.Add(new DictionaryEntry(title, old));
        fake.DictionaryEntries.Add(new DictionaryEntry(blog, null));
        var diff = SchemaDiffEngine.Compare(
            new SchemaSnapshot
            {
                DictionaryItems = [Dictionary(blog, "Blog"), Dictionary(title, "Title", blog)],
            },
            new SchemaSnapshot
            {
                DictionaryItems =
                [
                    Dictionary(old, "Old"),
                    Dictionary(title, "Title", old),
                    Dictionary(blog, "Blog"),
                ],
            }
        );

        // Act
        var result = await Apply(fake, diff, prune: true);

        // Assert: the move runs before the delete, so the child is not lost.
        Assert.Multiple(
            () => Assert.True(result.IsSuccess, result.ErrorMessage),
            () => Assert.Equal((title, (Guid?)blog), Assert.Single(fake.DictionaryItemsMoved)),
            () => Assert.Equal([old], fake.DictionaryItemsDeleted)
        );
    }

    [Fact]
    public async Task ApplyAsync_PruneDictionaryWhenTheTreeCannotBeRead_IsRefused()
    {
        var parent = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient
        {
            DictionaryEntriesFailure = UmbracoResponse<IReadOnlyList<DictionaryEntry>>.Failure(
                500,
                "boom"
            ),
        };
        var diff = Diff() with
        {
            DictionaryItems = Removes(
                Removed(SchemaKinds.DictionaryItem, "Blog", parent, Dictionary(parent, "Blog"))
            ),
        };

        await Assert.ThrowsAsync<SafetyRefusalException>(() => Apply(fake, diff, prune: true));
    }
}
