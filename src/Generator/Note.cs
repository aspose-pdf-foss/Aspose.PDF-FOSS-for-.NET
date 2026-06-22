using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// A footnote or endnote attached to a <see cref="TextFragment"/>.
/// Stored only; the layout engine does not currently render the note
/// glyph or the page-bottom note text.
/// </summary>
public sealed class Note
{
    public Note() { }

    public Note(string content) { Text = content; }

    /// <summary>Plain-text content of the note.</summary>
    public string? Text { get; set; }

    /// <summary>Rich-paragraph content of the note (when set, takes
    /// precedence over <see cref="Text"/>).</summary>
    public Paragraphs Paragraphs { get; set; } = new Paragraphs();

    /// <summary>Default text state applied to the note's plain-text content.</summary>
    public TextState TextState { get; set; } = new TextState();
}
