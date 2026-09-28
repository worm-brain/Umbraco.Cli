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
        // key is the picker entry's own new id; mediaKey is the media item's id.
        ["Umbraco.MediaPicker3"] = new(
            "Image media picker",
            """[{"key":"<new guid>","mediaKey":"<media id>","mediaTypeAlias":"Image","crops":[],"focalPoint":null}]"""
        ),
        ["Umbraco.DateTime"] = new("Date picker", "\"2026-05-01 00:00:00\""),
        ["Umbraco.DropDown.Flexible"] = new("Dropdown", """["News","Opinion"]"""),
        ["Umbraco.Tags"] = new("Tags", """["umbraco","cli"]"""),
        ["Umbraco.Integer"] = new("Numeric", "5"),
        ["Umbraco.TrueFalse"] = new("True/false", "true"),
        ["Umbraco.ContentPicker"] = new("Content picker", "\"<document id>\""),
    };

    /// <summary>A fresh example value for an editor, or null when the editor is not known.</summary>
    /// <param name="editorAlias">The data type's backend editor alias, or null when it could not be read.</param>
    /// <returns>A new JSON node (safe to add to a tree), or null.</returns>
    public static JsonNode? For(string? editorAlias) =>
        editorAlias is not null && ByEditorAlias.TryGetValue(editorAlias, out var example)
            ? JsonNode.Parse(example.Json)
            : null;
}
