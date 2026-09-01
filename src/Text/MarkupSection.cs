using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// A section of text on a page — a spatially coherent group of lines.
/// </summary>
public sealed class MarkupSection
{
    private List<MarkupParagraph> _paragraphs;
    private List<MarkupParagraph>? _commonParagraphs;

    internal MarkupSection(Rectangle rectangle, List<MarkupParagraph> paragraphs)
    {
        Rectangle = rectangle;
        _paragraphs = paragraphs;
        _commonParagraphs = new List<MarkupParagraph>(paragraphs);
    }

    /// <summary>The bounding rectangle of this section.</summary>
    public Rectangle Rectangle { get; internal set; }

    /// <summary>The paragraphs within this section.</summary>
    public List<MarkupParagraph> Paragraphs => _paragraphs;

    /// <summary>All TextFragments composing this section, flattened across paragraphs.</summary>
    public List<TextFragment> Fragments
    {
        get
        {
            var all = new List<TextFragment>();
            foreach (var p in _paragraphs)
                foreach (var f in p.Fragments) all.Add(f);
            return all;
        }
    }

    internal void ReplaceParagraph(int index, MarkupParagraph replacement)
    {
        _paragraphs[index] = replacement;
    }

    internal void RemoveParagraph(int index)
    {
        _paragraphs.RemoveAt(index);
    }

    /// <summary>Put the section back to the paragraphs found at absorb time.</summary>
    internal void RestoreCommonParagraphs() => _paragraphs = new List<MarkupParagraph>(_commonParagraphs!);
}
