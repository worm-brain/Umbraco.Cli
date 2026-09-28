using System.Text.Json.Nodes;

namespace Umbraco.Cli.Commands.Content;

/// <summary>
/// The <c>value</c> shape each property editor takes in a content <c>--json-body</c>, keyed by the
/// data type's backend <c>editorAlias</c> (#174). This is the single source for those formats:
/// <c>content create --example</c> fills its values from it, and the table in
/// <c>docs/commands.md</c> ("Property value formats") must list the same editors, which a test
/// checks.
/// <para>
/// The shapes were verified by creating content on Umbraco 17.7.0 and reading it back. An editor
/// not listed here (a block editor, a package's custom editor) gets a null value, so an example
/// never guesses a shape; the entry's <c>editorAlias</c> says which editor to look up.
/// </para>
/// </summary>
public static class PropertyValueExamples
{
    /// <summary>One editor's example.</summary>
    /// <param name="Editor">The editor as the backoffice names it, for the docs table.</param>
    /// <param name="Json">The example value, as JSON text.</param>
    public sealed record Example(string Editor, string Json);

    /// <summary>The known editors, by backend <c>editorAlias</c> (matched ignoring case).</summary>
    public static readonly IReadOnlyDictionary<string, Example> ByEditorAlias = new Dictionary<
        string,
        Example
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["Umbraco.TextBox"] = new("Textstring", "\"some text\""),
        ["Umbraco.TextArea"] = new("Textarea", "\"some text\""),
        ["Umbraco.RichText"] = new(
            "Rich text (Tiptap)",
            """{"markup":"<p>some text</p>","blocks":null}"""
        ),
        // key is the picker entry's own new id, which For fills in; mediaKey is the media item's
        // id, which only the caller knows.
        ["Umbraco.MediaPicker3"] = new(
            "Image media picker",
            $$"""[{"key":"{{NewGuid}}","mediaKey":"<media id>","mediaTypeAlias":"Image","crops":[],"focalPoint":null}]"""
        ),
        ["Umbraco.DateTime"] = new("Date picker", "\"2026-05-01 00:00:00\""),
        ["Umbraco.DropDown.Flexible"] = new("Dropdown", """["News","Opinion"]"""),
        ["Umbraco.Tags"] = new("Tags", """["umbraco","cli"]"""),
        ["Umbraco.Integer"] = new("Numeric", "5"),
        ["Umbraco.TrueFalse"] = new("True/false", "true"),
        ["Umbraco.ContentPicker"] = new("Content picker", "\"<document id>\""),
    };

    /// <summary>
    /// A fresh example value for an editor, or null when the editor is not known. Every
    /// <see cref="NewGuid"/> in the example is replaced by its own new GUID, so the value can be
    /// sent as it is; the other <c>&lt;...&gt;</c> placeholders (a media or document id) are left for
    /// the caller to replace.
    /// </summary>
    /// <param name="editorAlias">The data type's backend editor alias, or null when it could not be read.</param>
    /// <returns>A new JSON node (safe to add to a tree), or null.</returns>
    public static JsonNode? For(string? editorAlias)
    {
        if (editorAlias is null || !ByEditorAlias.TryGetValue(editorAlias, out var example))
            return null;
        var node = JsonNode.Parse(example.Json);
        FillNewGuids(node);
        return node;
    }

    /// <summary>
    /// The token an example uses for an id the caller need not choose (a picker entry's own
    /// <c>key</c>). The docs table shows it as written; <see cref="For"/> replaces it with a real
    /// GUID. Umbraco answers 500, not a validation error, when it is sent unreplaced, so it must
    /// never reach the output.
    /// </summary>
    public const string NewGuid = "<new guid>";

    /// <summary>
    /// Replaces, in place, every string value equal to <see cref="NewGuid"/> with a new GUID, one
    /// per occurrence so two entries never share a key.
    /// </summary>
    /// <param name="node">The example value; null is left alone.</param>
    private static void FillNewGuids(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot the keys: the loop assigns into the object it walks.
                foreach (var key in obj.Select(p => p.Key).ToList())
                    if (IsNewGuidToken(obj[key]))
                        obj[key] = Guid.NewGuid().ToString();
                    else
                        FillNewGuids(obj[key]);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    if (IsNewGuidToken(array[i]))
                        array[i] = Guid.NewGuid().ToString();
                    else
                        FillNewGuids(array[i]);
                break;
        }
    }

    /// <summary>Whether a node is the <see cref="NewGuid"/> string.</summary>
    /// <param name="node">The node.</param>
    /// <returns>True for the token, false for anything else (including null).</returns>
    private static bool IsNewGuidToken(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) && s == NewGuid;
}
