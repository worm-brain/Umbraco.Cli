using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.UserGroups;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <c>user-group update</c> merges (docs/conventions.md 5.1): the API takes the whole group, so the
/// command lays the given options over the current group and an omitted one keeps its value.
/// </summary>
public class UserGroupMergeTests
{
    private static readonly Guid Blog = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly UserGroupResponse Editors = new()
    {
        Id = Guid.NewGuid(),
        Alias = "editors",
        Name = "Editors",
        Icon = "icon-users",
        Sections = ["Umb.Section.Content"],
        Languages = ["en-US"],
        FallbackPermissions = ["Umb.Document.Read"],
        HasAccessToAllLanguages = true,
        DocumentStartNode = Blog,
        MediaRootAccess = true,
    };

    /// <summary>Nothing given: every shared option omitted.</summary>
    private static readonly UserGroupsCommand.SharedGroupValues NothingGiven = new(
        null,
        null,
        [],
        [],
        [],
        null,
        null,
        null,
        null,
        null
    );

    [Fact]
    public void Merge_OnlyANewName_KeepsEverythingElse()
    {
        var request = UserGroupsCommand.Merge(Editors, null, "Site editors", NothingGiven);

        Assert.Equal(
            (
                "editors",
                "Site editors",
                "icon-users",
                "Umb.Section.Content",
                true,
                (Guid?)Blog,
                true
            ),
            (
                request.Alias,
                request.Name,
                request.Icon,
                Assert.Single(request.Sections),
                request.HasAccessToAllLanguages,
                request.DocumentStartNode,
                request.MediaRootAccess
            )
        );
    }

    [Fact]
    public void Merge_FlagGivenFalse_ClearsIt()
    {
        var request = UserGroupsCommand.Merge(
            Editors,
            null,
            null,
            NothingGiven with
            {
                HasAccessToAllLanguages = false,
            }
        );

        Assert.False(request.HasAccessToAllLanguages);
    }

    [Fact]
    public void Merge_RootAccessGiven_ClearsTheStartNode()
    {
        // Root access and a start node say opposite things; setting one clears the other.
        var request = UserGroupsCommand.Merge(
            Editors,
            null,
            null,
            NothingGiven with
            {
                DocumentRootAccess = true,
            }
        );

        Assert.Equal((true, (Guid?)null), (request.DocumentRootAccess, request.DocumentStartNode));
    }

    [Fact]
    public void Merge_StartNodeGiven_ClearsRootAccess()
    {
        var node = Guid.NewGuid();

        var request = UserGroupsCommand.Merge(
            Editors,
            null,
            null,
            NothingGiven with
            {
                MediaStartNode = node,
            }
        );

        Assert.Equal((false, (Guid?)node), (request.MediaRootAccess, request.MediaStartNode));
    }

    [Fact]
    public void Merge_ListGiven_ReplacesTheList()
    {
        var request = UserGroupsCommand.Merge(
            Editors,
            null,
            null,
            NothingGiven with
            {
                Sections = ["Umb.Section.Media"],
            }
        );

        Assert.Equal(["Umb.Section.Media"], request.Sections);
    }

    // ── document permissions (#111) ───────────────────────────────────────────

    private static readonly Guid News = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Editors with Read on Blog and Read on News.</summary>
    private static readonly UserGroupResponse WithPermissions = Editors with
    {
        DocumentPermissions =
        [
            new DocumentPermission { Document = Blog, Verbs = ["Umb.Document.Read"] },
            new DocumentPermission { Document = News, Verbs = ["Umb.Document.Read"] },
        ],
    };

    [Fact]
    public void Merge_DocumentPermissionGiven_ReplacesThatDocumentAndKeepsTheOthers()
    {
        var request = UserGroupsCommand.Merge(
            WithPermissions,
            null,
            null,
            NothingGiven with
            {
                DocumentPermissions =
                [
                    new DocumentPermission { Document = Blog, Verbs = ["Umb.Document.Update"] },
                ],
            }
        );

        Assert.Equal(
            [$"{News}:Umb.Document.Read", $"{Blog}:Umb.Document.Update"],
            request.DocumentPermissions!.Select(p => $"{p.Document}:{string.Join(",", p.Verbs)}")
        );
    }

    [Fact]
    public void Merge_DocumentPermissionWithNoVerbs_RemovesThatDocument()
    {
        var request = UserGroupsCommand.Merge(
            WithPermissions,
            null,
            null,
            NothingGiven with
            {
                DocumentPermissions = [new DocumentPermission { Document = Blog }],
            }
        );

        Assert.Equal([News], request.DocumentPermissions!.Select(p => p.Document));
    }

    [Fact]
    public void Merge_NoDocumentPermissionGiven_LeavesThemToTheClient()
    {
        var request = UserGroupsCommand.Merge(WithPermissions, null, "Renamed", NothingGiven);

        Assert.Null(request.DocumentPermissions);
    }

    [Fact]
    public void ParseDocumentPermissions_VerbList_IsTrimmedAndDeduplicated()
    {
        var parsed = UserGroupsCommand.ParseDocumentPermissions([
            $"{Blog}=Umb.Document.Read, Umb.Document.Update,Umb.Document.Read",
        ]);

        Assert.Equal(["Umb.Document.Read", "Umb.Document.Update"], Assert.Single(parsed).Verbs);
    }

    [Fact]
    public void ParseDocumentPermissions_TwoDocuments_KeepsOnePermissionEachInOrder()
    {
        // A document named twice is refused at parse time (#444), so each value is its own entry.
        var parsed = UserGroupsCommand.ParseDocumentPermissions([
            $"{Blog}=Umb.Document.Read",
            $"{News}=Umb.Document.Delete",
        ]);

        Assert.Equal([Blog, News], parsed.Select(p => p.Document));
    }

    [Fact]
    public void ParseDocumentPermissions_NoVerbs_KeepsAnEmptyEntry()
    {
        var parsed = UserGroupsCommand.ParseDocumentPermissions([$"{Blog}="]);

        Assert.Empty(Assert.Single(parsed).Verbs);
    }
}
