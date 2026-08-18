using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// A footnote or endnote attached to a <see cref="TextFragment"/>.
/// Rendered as a superscript reference marker after the parent fragment
/// and a marker-prefixed note body at the bottom of the parent's page.
/// </summary>
public sealed class Note
{
    public Note() { }

    /// <summary>Content passed here becomes the note's body paragraph;
    /// the reference marker stays auto-numbered unless <see cref="Text"/>
    /// is set (setting Text never replaces this body).</summary>
    public Note(string content) { Paragraphs.Add(new TextFragment(content)); }

    /// <summary>Custom reference-marker label. When unset, the marker is the
    /// footnote's sequential number.</summary>
    public string? Text { get; set; }

    /// <summary>Rich-paragraph content of the note (when set, takes
    /// precedence over <see cref="Text"/>).</summary>
    public Paragraphs Paragraphs { get; set; } = new Paragraphs();

    /// <summary>Default text state applied to the note's plain-text content.</summary>
    public TextState TextState { get; set; } = new TextState();
}
