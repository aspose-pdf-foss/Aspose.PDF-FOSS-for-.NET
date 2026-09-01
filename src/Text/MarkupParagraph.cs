using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// A paragraph within a markup section.
/// </summary>
public sealed class MarkupParagraph
{
    private readonly List<List<TextFragment>> _lines;

    internal MarkupParagraph(string text, Point[] points, List<List<TextFragment>> lines)
    {
        _text = text; // set backing field directly to avoid triggering fragment replacement
        Points = points;
        _lines = lines;
    }

    /// <summary>Replace the cached text without touching the underlying fragments
    /// (used by the absorber's space-glyph reassembly pass).</summary>
    internal void RefreshText(string text) => _text = text;

    /// <summary>
    /// The text of this paragraph. Lines are separated by \r\n.
    /// Setting this replaces the text in the underlying fragments via TextReplacer.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
                return;

            // Replace text in the underlying fragments.
            // Strategy: put the full new text into the first fragment,
            // clear all subsequent fragments.
            var first = true;
            foreach (var line in _lines)
            {
                foreach (var fragment in line)
                {
                    if (first)
                    {
                        fragment.Text = value;
                        first = false;
                    }
                    else
                    {
                        fragment.Text = "";
                    }
                }
            }

            _text = value;
        }
    }
    private string _text;

    /// <summary>
    /// The bounding polygon points: [LL, LR, UR, UL].
    /// </summary>
    public Point[] Points { get; }

    /// <summary>
    /// The lines of text fragments that compose this paragraph.
    /// </summary>
    public List<List<TextFragment>> Lines => _lines;

    /// <summary>
    /// Secondary points for cross-page paragraph continuation.
    /// </summary>
    public List<Point[]> SecondaryPoints { get; internal set; } = new();

    /// <summary>
    /// Page numbers for cross-page paragraph continuation.
    /// </summary>
    public List<int> ContinuationPageNumbers { get; internal set; } = new();

    /// <summary>All TextFragments composing this paragraph, flattened across lines.</summary>
    public List<TextFragment> Fragments
    {
        get
        {
            var all = new List<TextFragment>();
            foreach (var line in _lines)
                foreach (var f in line) all.Add(f);
            return all;
        }
    }
}
