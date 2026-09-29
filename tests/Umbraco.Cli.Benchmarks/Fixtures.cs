using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cli.Client;
using Umbraco.Cli.Commands.Content;
using Umbraco.Cli.Commands.Schema;

namespace Umbraco.Cli.Benchmarks;

/// <summary>
/// Synthetic data shaped like Umbraco 17 Management API bodies, built in memory so no benchmark
/// needs a live instance. Every value is derived from its index (ids, names, dates), never from
/// the clock or a random source: a benchmark run on a PR and one on <c>main</c> see byte-identical
/// input, which is what lets the CI gate compare their allocations.
/// </summary>
internal static class Fixtures
{
    // Id namespaces: the second GUID component, so a document and a data type with the same
    // index never share an id.
    private const short Document = 1;
    private const short DocumentType = 2;
    private const short DataType = 3;
    private const short Template = 4;
    private const short DictionaryItem = 5;
    private const short Container = 6;
    private const short Property = 7;
    private const short Block = 8;

    /// <summary>How many document types the content fixtures spread their documents across.</summary>
    private const int DocumentTypeCount = 12;

    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A stable id: the same kind and index always give the same GUID.</summary>
    /// <param name="kind">The id namespace, one of the constants above.</param>
    /// <param name="index">The entity's index within its kind.</param>
    /// <returns>The id.</returns>
    private static Guid Id(short kind, int index) => new(index, kind, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// The parent of document <paramref name="i"/> in a ten-way tree: document 0 is the only root,
    /// and every parent's index is lower than its children's, so the documents are in an order
    /// that never creates a child before its parent.
    /// </summary>
    /// <param name="i">The document index.</param>
    /// <returns>The parent's index, or null for the root.</returns>
    private static int? ParentOf(int i) => i == 0 ? null : (i - 1) / 10;

    // ── Output: the DTOs a list command writes ──────────────────────────────────

    /// <summary>
    /// <paramref name="count"/> content list rows as <c>content list --all</c> returns them: the
    /// tree fields plus a document-type alias, two culture variants and no property values.
    /// </summary>
    /// <param name="count">How many rows.</param>
    /// <returns>The rows.</returns>
    public static List<ContentItemResponse> ContentItems(int count) =>
        [
            .. Enumerable
                .Range(0, count)
                .Select(i => new ContentItemResponse
                {
                    Id = Id(Document, i),
                    Name = $"Page {i}",
                    DocumentType = new ContentTypeRef
                    {
                        Id = Id(DocumentType, i % DocumentTypeCount),
                        Alias = $"docType{i % DocumentTypeCount}",
                        Icon = "icon-document",
                    },
                    Parent = ParentOf(i) is { } p
                        ? new ContentParentReference { Id = Id(Document, p) }
                        : null,
                    IsPublished = i % 3 != 0,
                    CreateDate = Epoch.AddMinutes(i),
                    Flags = [],
                    Variants =
                    [
                        new ContentVariantResponse
                        {
                            Culture = "en-US",
                            Name = $"Page {i}",
                            State = i % 3 != 0 ? "Published" : "Draft",
                            CreateDate = Epoch.AddMinutes(i),
                        },
                        new ContentVariantResponse
                        {
                            Culture = "da-DK",
                            Name = $"Side {i}",
                            State = "Draft",
                            CreateDate = Epoch.AddMinutes(i),
                        },
                    ],
                }),
        ];

    // ── Kiota: raw response bodies ─────────────────────────────────────────────

    /// <summary>
    /// A <c>GET /tree/document/children</c> page of <paramref name="count"/> items, as UTF-8 JSON.
    /// </summary>
    /// <param name="count">How many tree items.</param>
    /// <returns>The response body.</returns>
    public static byte[] DocumentTreePage(int count) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new JsonObject
            {
                ["total"] = count,
                ["items"] = new JsonArray([.. Enumerable.Range(0, count).Select(TreeItem)]),
            }
        );

    /// <summary>One document tree item.</summary>
    /// <param name="i">The document index.</param>
    /// <returns>The item.</returns>
    private static JsonNode TreeItem(int i) =>
        new JsonObject
        {
            ["id"] = Id(Document, i).ToString(),
            ["parent"] = ParentOf(i) is { } p
                ? new JsonObject { ["id"] = Id(Document, p).ToString() }
                : null,
            ["ancestors"] = ParentOf(i) is { } a
                ? new JsonArray(new JsonObject { ["id"] = Id(Document, a).ToString() })
                : new JsonArray(),
            ["hasChildren"] = i % 4 == 0,
            ["isTrashed"] = false,
            ["isProtected"] = false,
            ["noAccess"] = false,
            ["createDate"] = Epoch.AddMinutes(i),
            ["documentType"] = DocumentTypeReference(i),
            ["flags"] = new JsonArray(),
            ["variants"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = $"Page {i}",
                    ["culture"] = "en-US",
                    ["state"] = "Published",
                    ["flags"] = new JsonArray(),
                },
                new JsonObject
                {
                    ["name"] = $"Side {i}",
                    ["culture"] = "da-DK",
                    ["state"] = "Draft",
                    ["flags"] = new JsonArray(),
                }
            ),
        };

    /// <summary>
    /// A <c>GET /document/{id}</c> body with <paramref name="extraValues"/> text properties on
    /// top of the standard set, as UTF-8 JSON: the shape the raw content path and Kiota's typed
    /// model both read.
    /// </summary>
    /// <param name="extraValues">How many additional text values to add.</param>
    /// <returns>The response body.</returns>
    public static byte[] LargeDocument(int extraValues) =>
        JsonSerializer.SerializeToUtf8Bytes(DocumentBody(0, extraValues));

    // ── Content pipeline: export / diff / apply ─────────────────────────────────

    /// <summary>
    /// A desired and a live content snapshot over the same tree of <paramref name="count"/>
    /// documents, differing the way a real promotion does: 1 in 20 documents only in the snapshot
    /// (added), 1 in 20 only live (a prune candidate), 1 in 10 with an edited value, 1 in 25
    /// published in the snapshot but draft live, and the rest identical.
    /// </summary>
    /// <param name="count">How many documents the tree holds before any are added or removed.</param>
    /// <returns>The two snapshots.</returns>
    public static (ContentSnapshot Desired, ContentSnapshot Current) ContentSnapshots(int count)
    {
        var desired = new List<ContentNode>();
        var current = new List<ContentNode>();
        for (var i = 0; i < count; i++)
        {
            var node = ContentNodeAt(i);
            if (i % 20 != 19)
                desired.Add(node);
            if (i % 20 == 18)
                continue;

            var live = node.Body.DeepClone();
            if (i % 10 == 3)
                live["values"]![0]!["value"] = $"Old title {i}";
            if (i % 25 == 7)
                live["variants"]![0]!["state"] = "Draft";
            current.Add(
                new ContentNode
                {
                    Id = node.Id,
                    Parent = node.Parent,
                    Body = live,
                }
            );
        }
        return (
            new ContentSnapshot { Documents = desired },
            new ContentSnapshot { Documents = current }
        );
    }

    /// <summary>Document <paramref name="i"/> as a content snapshot entry.</summary>
    /// <param name="i">The document index.</param>
    /// <returns>The entry.</returns>
    private static ContentNode ContentNodeAt(int i) =>
        new()
        {
            Id = Id(Document, i),
            Parent = ParentOf(i) is { } p ? Id(Document, p) : null,
            Body = DocumentBody(i, extraValues: 0),
        };

    /// <summary>
    /// A document body with a realistic property mix: culture-variant titles, rich text, tags, a
    /// number, a toggle, a read-only label and a block list, then <paramref name="extraValues"/>
    /// plain text values.
    /// </summary>
    /// <param name="i">The document index.</param>
    /// <param name="extraValues">How many additional text values to add.</param>
    /// <returns>The body.</returns>
    private static JsonObject DocumentBody(int i, int extraValues)
    {
        var values = new JsonArray(
            Value("title", "en-US", $"Page {i}", "Umbraco.TextBox"),
            Value("title", "da-DK", $"Side {i}", "Umbraco.TextBox"),
            Value(
                "bodyText",
                "en-US",
                new JsonObject
                {
                    ["markup"] = $"<p>Body copy for page {i}, with <strong>markup</strong>.</p>",
                    ["blocks"] = null,
                },
                "Umbraco.RichText"
            ),
            Value("tags", null, new JsonArray("news", "product", $"tag{i % 7}"), "Umbraco.Tags"),
            Value("price", null, 10 + i % 90, "Umbraco.Decimal"),
            Value("isFeatured", null, i % 4 == 0, "Umbraco.TrueFalse"),
            Value("createdBy", null, "Import job", "Umbraco.Label"),
            Value("blocks", "en-US", BlockList(i), "Umbraco.BlockList")
        );
        for (var k = 0; k < extraValues; k++)
            values.Add(
                Value($"field{k}", "en-US", $"Value {k} of document {i}", "Umbraco.TextBox")
            );

        return new JsonObject
        {
            ["id"] = Id(Document, i).ToString(),
            ["documentType"] = DocumentTypeReference(i),
            ["template"] = new JsonObject { ["id"] = Id(Template, i % 6).ToString() },
            ["isTrashed"] = false,
            ["flags"] = new JsonArray(),
            ["values"] = values,
            ["variants"] = new JsonArray(
                Variant(i, "en-US", $"Page {i}", "Published"),
                Variant(i, "da-DK", $"Side {i}", "Draft")
            ),
        };
    }

    /// <summary>A document body's <c>documentType</c> reference.</summary>
    /// <param name="i">The document index.</param>
    /// <returns>The reference.</returns>
    private static JsonObject DocumentTypeReference(int i) =>
        new()
        {
            ["id"] = Id(DocumentType, i % DocumentTypeCount).ToString(),
            ["icon"] = "icon-document",
            ["collection"] = null,
        };

    /// <summary>One entry of a document body's <c>values</c>.</summary>
    /// <param name="alias">The property alias.</param>
    /// <param name="culture">The culture, or null for an invariant property.</param>
    /// <param name="value">The value, already in its JSON form.</param>
    /// <param name="editorAlias">The property editor behind the value.</param>
    /// <returns>The value entry.</returns>
    private static JsonObject Value(
        string alias,
        string? culture,
        JsonNode? value,
        string editorAlias
    ) =>
        new()
        {
            ["alias"] = alias,
            ["culture"] = culture,
            ["segment"] = null,
            ["value"] = value,
            ["editorAlias"] = editorAlias,
        };

    /// <summary>One entry of a document body's <c>variants</c>.</summary>
    /// <param name="i">The document index, which dates the variant.</param>
    /// <param name="culture">The variant's culture.</param>
    /// <param name="name">The variant's name.</param>
    /// <param name="state">The publication state, e.g. <c>Published</c> or <c>Draft</c>.</param>
    /// <returns>The variant.</returns>
    private static JsonObject Variant(int i, string culture, string name, string state) =>
        new()
        {
            ["culture"] = culture,
            ["segment"] = null,
            ["name"] = name,
            ["state"] = state,
            ["createDate"] = Epoch.AddMinutes(i),
            ["updateDate"] = Epoch.AddMinutes(i + 1),
            ["publishDate"] = state == "Published" ? Epoch.AddMinutes(i + 2) : null,
            ["scheduledPublishDate"] = null,
            ["scheduledUnpublishDate"] = null,
            ["flags"] = new JsonArray(),
        };

    /// <summary>A two-block Block List value, the nested shape most real sites carry.</summary>
    /// <param name="i">The document index.</param>
    /// <returns>The value.</returns>
    private static JsonObject BlockList(int i)
    {
        var keys = new[] { Id(Block, i * 2), Id(Block, i * 2 + 1) };
        return new JsonObject
        {
            ["layout"] = new JsonObject
            {
                ["Umbraco.BlockList"] = new JsonArray([
                    .. keys.Select(k => new JsonObject { ["contentKey"] = k.ToString() }),
                ]),
            },
            ["contentData"] = new JsonArray([
                .. keys.Select(k => new JsonObject
                {
                    ["key"] = k.ToString(),
                    ["contentTypeKey"] = Id(DocumentType, 100).ToString(),
                    ["values"] = new JsonArray(
                        new JsonObject { ["alias"] = "heading", ["value"] = $"Block heading {i}" },
                        new JsonObject { ["alias"] = "text", ["value"] = $"Block text for {k}" }
                    ),
                }),
            ]),
            ["settingsData"] = new JsonArray(),
            ["expose"] = new JsonArray([
                .. keys.Select(k => new JsonObject
                {
                    ["contentKey"] = k.ToString(),
                    ["culture"] = "en-US",
                    ["segment"] = null,
                }),
            ]),
        };
    }

    // ── Schema pipeline: export / diff / apply ──────────────────────────────────

    /// <summary>
    /// A desired and a live schema snapshot of a mid-sized site: <paramref name="documentTypes"/>
    /// document types of 20 properties each, 60 data types, 40 templates, 3 languages and 300
    /// dictionary items. The live side differs as a promotion would: 1 in 10 document types has a
    /// renamed property, 1 in 30 is new in the snapshot, 1 in 30 exists only live, and 1 in 12 data
    /// types has a changed configuration.
    /// </summary>
    /// <param name="documentTypes">How many document types.</param>
    /// <returns>The two snapshots.</returns>
    public static (SchemaSnapshot Desired, SchemaSnapshot Current) SchemaSnapshots(
        int documentTypes
    )
    {
        var desired = new SchemaSnapshot();
        var current = new SchemaSnapshot();

        for (var i = 0; i < 60; i++)
        {
            var dt = DataTypeBody(i);
            desired.DataTypes!.Add(dt);
            var live = dt.DeepClone();
            if (i % 12 == 5)
                live["values"]![0]!["value"] = 250;
            current.DataTypes!.Add(live);
        }

        for (var i = 0; i < 40; i++)
        {
            desired.Templates!.Add(TemplateBody(i));
            current.Templates!.Add(TemplateBody(i));
        }

        for (var i = 0; i < documentTypes; i++)
        {
            var body = DocumentTypeBody(i);
            if (i % 30 != 29)
                desired.DocumentTypes!.Add(body);
            if (i % 30 == 28)
                continue;
            var live = body.DeepClone();
            if (i % 10 == 4)
                live["properties"]![3]!["name"] = "Renamed property";
            current.DocumentTypes!.Add(live);
        }

        foreach (
            var (iso, name, isDefault) in new[]
            {
                ("en-US", "English (United States)", true),
                ("da-DK", "Dansk (Danmark)", false),
                ("de-DE", "Deutsch (Deutschland)", false),
            }
        )
        {
            var language = new JsonObject
            {
                ["isoCode"] = iso,
                ["name"] = name,
                ["isDefault"] = isDefault,
                ["isMandatory"] = isDefault,
                ["fallbackIsoCode"] = null,
            };
            desired.Languages!.Add(language);
            current.Languages!.Add(language.DeepClone());
        }

        for (var i = 0; i < 300; i++)
        {
            desired.DictionaryItems!.Add(DictionaryBody(i));
            current.DictionaryItems!.Add(DictionaryBody(i));
        }

        return (desired, current);
    }

    /// <summary>A data type body: a text box with a max-length configuration.</summary>
    /// <param name="i">The data type index.</param>
    /// <returns>The body.</returns>
    private static JsonObject DataTypeBody(int i) =>
        new()
        {
            ["id"] = Id(DataType, i).ToString(),
            ["name"] = $"Text box {i}",
            ["editorAlias"] = "Umbraco.TextBox",
            ["editorUiAlias"] = "Umb.PropertyEditorUi.TextBox",
            ["parent"] = null,
            ["values"] = new JsonArray(
                new JsonObject { ["alias"] = "maxChars", ["value"] = 500 },
                new JsonObject { ["alias"] = "inputType", ["value"] = "text" }
            ),
            ["isDeletable"] = true,
            ["canIgnoreStartNodes"] = false,
        };

    /// <summary>A template body with a few lines of Razor.</summary>
    /// <param name="i">The template index.</param>
    /// <returns>The body.</returns>
    private static JsonObject TemplateBody(int i) =>
        new()
        {
            ["id"] = Id(Template, i).ToString(),
            ["name"] = $"Template {i}",
            ["alias"] = $"template{i}",
            ["content"] =
                "@using Umbraco.Cms.Web.Common.PublishedModels;\n"
                + "@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage\n"
                + "@{ Layout = \"master.cshtml\"; }\n"
                + $"<main class=\"template-{i}\">@Model.Value(\"bodyText\")</main>\n",
            ["masterTemplate"] =
                i == 0 ? null : new JsonObject { ["id"] = Id(Template, 0).ToString() },
        };

    /// <summary>
    /// A document type body with two containers, 20 properties over the fixture's data types,
    /// one composition, allowed children and templates.
    /// </summary>
    /// <param name="i">The document type index.</param>
    /// <returns>The body.</returns>
    private static JsonObject DocumentTypeBody(int i)
    {
        var tab = Id(Container, i * 2);
        var group = Id(Container, i * 2 + 1);
        return new JsonObject
        {
            ["id"] = Id(DocumentType, i).ToString(),
            ["alias"] = $"docType{i}",
            ["name"] = $"Document type {i}",
            ["description"] = $"Pages of kind {i}.",
            ["icon"] = "icon-document",
            ["allowedAsRoot"] = i < 5,
            ["variesByCulture"] = true,
            ["variesBySegment"] = false,
            ["isElement"] = i % 5 == 0,
            ["collection"] = null,
            ["properties"] = new JsonArray([
                .. Enumerable
                    .Range(0, 20)
                    .Select(j => new JsonObject
                    {
                        ["id"] = Id(Property, i * 20 + j).ToString(),
                        ["container"] = new JsonObject
                        {
                            ["id"] = (j < 10 ? tab : group).ToString(),
                        },
                        ["alias"] = $"prop{j}",
                        ["name"] = $"Property {j}",
                        ["description"] = null,
                        ["dataType"] = new JsonObject
                        {
                            ["id"] = Id(DataType, (i + j) % 60).ToString(),
                        },
                        ["variesByCulture"] = j % 2 == 0,
                        ["variesBySegment"] = false,
                        ["sortOrder"] = j,
                        ["validation"] = new JsonObject
                        {
                            ["mandatory"] = j == 0,
                            ["mandatoryMessage"] = null,
                            ["regEx"] = null,
                            ["regExMessage"] = null,
                        },
                        ["appearance"] = new JsonObject { ["labelOnTop"] = false },
                    }),
            ]),
            ["containers"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = tab.ToString(),
                    ["parent"] = null,
                    ["name"] = "Content",
                    ["type"] = "Tab",
                    ["sortOrder"] = 0,
                },
                new JsonObject
                {
                    ["id"] = group.ToString(),
                    ["parent"] = new JsonObject { ["id"] = tab.ToString() },
                    ["name"] = "SEO",
                    ["type"] = "Group",
                    ["sortOrder"] = 1,
                }
            ),
            ["compositions"] =
                i == 0
                    ? new JsonArray()
                    : new JsonArray(
                        new JsonObject
                        {
                            ["documentType"] = new JsonObject
                            {
                                ["id"] = Id(DocumentType, 0).ToString(),
                            },
                            ["compositionType"] = "Composition",
                        }
                    ),
            ["allowedDocumentTypes"] = new JsonArray(
                new JsonObject
                {
                    ["documentType"] = new JsonObject
                    {
                        ["id"] = Id(DocumentType, (i + 1) % 30).ToString(),
                    },
                    ["sortOrder"] = 0,
                }
            ),
            ["allowedTemplates"] = new JsonArray(
                new JsonObject { ["id"] = Id(Template, i % 40).ToString() }
            ),
            ["defaultTemplate"] = new JsonObject { ["id"] = Id(Template, i % 40).ToString() },
            ["cleanup"] = new JsonObject
            {
                ["preventCleanup"] = false,
                ["keepAllVersionsNewerThanDays"] = null,
                ["keepLatestVersionPerDayForDays"] = null,
            },
        };
    }

    /// <summary>A dictionary item body with three translations; 1 in 10 are nested under another.</summary>
    /// <param name="i">The dictionary item index.</param>
    /// <returns>The body.</returns>
    private static JsonObject DictionaryBody(int i) =>
        new()
        {
            ["id"] = Id(DictionaryItem, i).ToString(),
            ["name"] = $"Site.Key{i}",
            ["parent"] =
                i % 10 == 9
                    ? new JsonObject { ["id"] = Id(DictionaryItem, i - 1).ToString() }
                    : null,
            ["translations"] = new JsonArray(
                new JsonObject { ["isoCode"] = "da-DK", ["translation"] = $"Tekst {i}" },
                new JsonObject { ["isoCode"] = "de-DE", ["translation"] = $"Text {i} (de)" },
                new JsonObject { ["isoCode"] = "en-US", ["translation"] = $"Text {i}" }
            ),
        };
}
