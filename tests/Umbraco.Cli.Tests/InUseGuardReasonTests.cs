using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands;

namespace Umbraco.Cli.Tests;

/// <summary>
/// <see cref="InUseGuard.ReasonAsync"/>, the one switch that decides whether a delete is unsafe
/// (#281): the same target with and without a prune plan, driven through the fake client.
/// </summary>
public class InUseGuardReasonTests
{
    private static readonly Guid TemplateId = Guid.NewGuid();
    private static readonly Guid PageType = Guid.NewGuid();

    /// <summary>A plan that deletes the given items and moves the given dictionary items.</summary>
    private static DeletePlanContext Plan(
        IEnumerable<(EntityKind, Guid)>? deleted = null,
        IEnumerable<Guid>? moved = null
    ) => new(deleted ?? [], moved ?? [], new Dictionary<Guid, JsonNode>(), []);

    private static Task<string?> Reason(
        FakeUmbracoManagementClient fake,
        DeleteTarget target,
        DeletePlanContext? plan
    ) => InUseGuard.ReasonAsync(fake, target, plan, CancellationToken.None);

    private static FakeUmbracoManagementClient TemplateUsedByPage()
    {
        var fake = new FakeUmbracoManagementClient();
        fake.TemplateUsers[TemplateId] = [new TemplateUser(PageType, "Page")];
        return fake;
    }

    [Fact]
    public async Task ReasonAsync_UsedTemplateWithoutPlan_NamesTheUser()
    {
        // Arrange
        var fake = TemplateUsedByPage();

        // Act
        var reason = await Reason(
            fake,
            new DeleteTarget.Item(EntityKind.Template, TemplateId),
            null
        );

        // Assert
        Assert.Contains("used by Page", reason);
    }

    [Fact]
    public async Task ReasonAsync_TemplateWhoseOnlyUserThePlanDeletes_IsSafe()
    {
        // Arrange
        var fake = TemplateUsedByPage();
        var plan = Plan(deleted: [(EntityKind.DocumentType, PageType)]);

        // Act
        var reason = await Reason(
            fake,
            new DeleteTarget.Item(EntityKind.Template, TemplateId),
            plan
        );

        // Assert
        Assert.Null(reason);
    }

    [Fact]
    public async Task ReasonAsync_TemplateUsageUnreadable_GivesTheSameMessageWithOrWithoutPlan()
    {
        // Arrange: the same question (the usage cannot be read) in both situations.
        var fake = new FakeUmbracoManagementClient
        {
            TemplateUsageFailure = UmbracoResponse<
                IReadOnlyDictionary<Guid, IReadOnlyList<TemplateUser>>
            >.Failure(500, "boom"),
        };
        var target = new DeleteTarget.Item(EntityKind.Template, TemplateId, "home");

        // Act
        var single = await Reason(fake, target, null);
        var prune = await Reason(fake, target, Plan());

        // Assert
        Assert.Equal(single, prune);
    }

    [Fact]
    public async Task ReasonAsync_SeveralTemplatesInOnePlan_ReadTheUsageOnce()
    {
        // Arrange
        var fake = TemplateUsedByPage();
        var plan = Plan();

        // Act
        await Reason(fake, new DeleteTarget.Item(EntityKind.Template, TemplateId), plan);
        await Reason(fake, new DeleteTarget.Item(EntityKind.Template, Guid.NewGuid()), plan);

        // Assert
        Assert.Equal(1, fake.TemplateUsageReads);
    }

    [Fact]
    public async Task ReasonAsync_DictionaryChildThePlanMovesAway_IsSafe()
    {
        // Arrange
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryEntries.Add(new DictionaryEntry(parent, null));
        fake.DictionaryEntries.Add(new DictionaryEntry(child, parent));

        // Act
        var reason = await Reason(
            fake,
            new DeleteTarget.Item(EntityKind.DictionaryItem, parent, "Old"),
            Plan(moved: [child])
        );

        // Assert
        Assert.Null(reason);
    }

    [Fact]
    public async Task ReasonAsync_DictionaryChildThePlanKeeps_IsRefusedNamingTheItem()
    {
        // Arrange
        var parent = Guid.NewGuid();
        var fake = new FakeUmbracoManagementClient();
        fake.DictionaryEntries.Add(new DictionaryEntry(parent, null));
        fake.DictionaryEntries.Add(new DictionaryEntry(Guid.NewGuid(), parent));

        // Act
        var reason = await Reason(
            fake,
            new DeleteTarget.Item(EntityKind.DictionaryItem, parent, "Old"),
            Plan()
        );

        // Assert
        Assert.Contains("'Old' has 1 child item(s) the snapshot keeps", reason);
    }

    [Fact]
    public async Task ReasonAsync_Language_AlwaysHasAReason()
    {
        // Act
        var reason = await Reason(
            new FakeUmbracoManagementClient(),
            new DeleteTarget.Language("da-DK"),
            null
        );

        // Assert
        Assert.Contains("Deleting language da-DK", reason);
    }

    [Fact]
    public async Task ReasonAsync_StaticFolder_IsNeverChecked()
    {
        // Act
        var reason = await Reason(
            new FakeUmbracoManagementClient(),
            new DeleteTarget.StaticFile(StaticFileKind.PartialView, "/blocks", IsFolder: true),
            Plan()
        );

        // Assert
        Assert.Null(reason);
    }

    [Fact]
    public async Task ReasonAsync_StaticFileALiveTemplateNames_WithoutPlan_IsRefused()
    {
        // Arrange: no plan means the templates as they stand.
        var fake = new FakeUmbracoManagementClient();
        fake.TemplateList.Add(new TemplateResponse { Id = TemplateId, Name = "Home" });
        fake.TemplateRaw[TemplateId] = new JsonObject
        {
            ["name"] = "Home",
            ["content"] = "@await Html.PartialAsync(\"header\")",
        };

        // Act
        var reason = await Reason(
            fake,
            new DeleteTarget.StaticFile(StaticFileKind.PartialView, "/header.cshtml", false),
            null
        );

        // Assert
        Assert.Contains("'Home'", reason);
    }
}
