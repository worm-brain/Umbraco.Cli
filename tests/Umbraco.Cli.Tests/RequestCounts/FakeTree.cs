using System.Text.Json.Nodes;
using System.Web;

namespace Umbraco.Cli.Tests;

/// <summary>
/// One Management API tree (documents, media, data types, ...) held in memory and served the way
/// Umbraco serves it: <c>tree/{kind}/root</c> and <c>tree/{kind}/children?parentId=</c>, each paged
/// by <c>skip</c> and <c>take</c> and reporting the level's <c>total</c>. <c>hasChildren</c> and
/// <c>parent</c> are filled in from the structure, so a fixture states only the nodes.
/// </summary>
internal sealed class FakeTree
{
    private readonly List<(Guid Id, Guid? Parent, JsonObject Item)> _nodes = [];

    /// <summary>Every node's id, in the order added.</summary>
    public IReadOnlyList<Guid> Ids => [.. _nodes.Select(n => n.Id)];

    /// <summary>How many nodes the tree holds.</summary>
    public int Count => _nodes.Count;

    /// <summary>Adds a node.</summary>
    /// <param name="id">The node id.</param>
    /// <param name="parent">The parent node, or null for the root level.</param>
    /// <param name="item">
    /// The kind-specific tree-item fields (a document's <c>variants</c>, a data type's
    /// <c>isFolder</c>); <c>id</c>, <c>parent</c> and <c>hasChildren</c> are set from the structure.
    /// </param>
    /// <returns>This tree, for chaining.</returns>
    public FakeTree Add(Guid id, Guid? parent, JsonObject item)
    {
        _nodes.Add((id, parent, item));
        return this;
    }

    /// <summary>
    /// Adds <paramref name="count"/> nodes under <paramref name="parent"/>, each built by
    /// <paramref name="item"/> from its index.
    /// </summary>
    /// <param name="count">How many nodes to add.</param>
    /// <param name="parent">Their parent, or null for the root level.</param>
    /// <param name="item">Builds a node's tree-item fields from its index.</param>
    /// <returns>The new nodes' ids, in order.</returns>
    public IReadOnlyList<Guid> AddMany(int count, Guid? parent, Func<int, JsonObject> item)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < count; i++)
            Add(ids[i], parent, item(i));
        return ids;
    }

    /// <summary>
    /// Answers a <c>root</c> or <c>children</c> request: the page of the level the query names.
    /// A <c>take</c> of zero or none is served as Umbraco's default of 100.
    /// </summary>
    /// <param name="request">The tree request.</param>
    /// <returns>The paged body, <c>{"total": n, "items": [...]}</c>.</returns>
    public string Page(HttpRequestMessage request)
    {
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        Guid? parent = Guid.TryParse(query["parentId"], out var p) ? p : null;
        var skip = int.TryParse(query["skip"], out var s) ? s : 0;
        var take = int.TryParse(query["take"], out var t) && t > 0 ? t : 100;

        var level = _nodes.Where(n => n.Parent == parent).ToList();
        var items = new JsonArray();
        foreach (var (id, parentId, item) in level.Skip(skip).Take(take))
        {
            var served = (JsonObject)item.DeepClone();
            served["id"] = id;
            served["parent"] = parentId is { } pid ? new JsonObject { ["id"] = pid } : null;
            served["hasChildren"] = _nodes.Any(n => n.Parent == id);
            items.Add(served);
        }
        return new JsonObject { ["total"] = level.Count, ["items"] = items }.ToJsonString();
    }
}
