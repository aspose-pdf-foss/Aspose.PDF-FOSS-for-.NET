namespace Aspose.Pdf.LogicalStructure;

/// <summary>What a <see cref="MarkedContentItem"/> paints.</summary>
public enum MarkedContentKind
{
    /// <summary>A run of text in one style, on one line.</summary>
    Text,
    /// <summary>An image XObject.</summary>
    Image,
    /// <summary>A drawing: painted paths, or a form XObject drawn whole, covering
    /// <see cref="MarkedContentItem.Rectangle"/> - a figure a reader can only show by rendering the page there.</summary>
    Drawing,
}

/// <summary>
/// A piece of page content a structure element marks: a run of text in one style on one line, or an
/// image. Returned by <see cref="StructureElement.GetMarkedContent"/> in reading order.
///
/// ⚠ FOSS-only: the reference exposes no read from a structure element to its content.
/// </summary>
public sealed class MarkedContentItem
{
    internal MarkedContentItem(MarkedContentKind kind, StructureElement? element, Page? page, int mcid)
    {
        Kind = kind;
        Element = element;
        Page = page;
        Mcid = mcid;
    }

    /// <summary>Whether the item is a text run, an image or a drawing.</summary>
    public MarkedContentKind Kind { get; }

    /// <summary>The structure element whose marked content holds the item: the element asked,
    /// or a descendant of it (a Link or Span inside a paragraph); null for an artifact's content
    /// (<see cref="Page.GetArtifactContent"/>).</summary>
    public StructureElement? Element { get; }

    /// <summary>The page the item is drawn on; null when the content is not in a page of the
    /// document.</summary>
    public Page? Page { get; }

    /// <summary>The marked-content identifier of the (first) sequence the item was read from.</summary>
    public int Mcid { get; }

    /// <summary>The run's text. A run that follows another after a word gap or on a new line
    /// starts with the space that separates them; an image has none.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Where in <see cref="Text"/> the page broke a word over its line with a hyphen that the text leaves out
    /// ("exam-" / "ple" reads "example", the break at 4): a word processor's optional hyphen stands there. Ascending;
    /// empty when the run breaks no word.
    ///
    /// ⚠ FOSS-only: the reference has no read from a structure element to its content.
    /// </summary>
    public IReadOnlyList<int> HyphenBreaks => _hyphenBreaks;
    private readonly List<int> _hyphenBreaks = new();

    /// <summary>Notes that the page broke a word with a hyphen at <paramref name="at"/> of <see cref="Text"/>.</summary>
    internal void NoteHyphenBreak(int at) => _hyphenBreaks.Add(at);

    /// <summary>The font the run is shown in; null for an image or an unknown font.</summary>
    public Aspose.Pdf.Text.Font? Font { get; internal set; }

    /// <summary>The font's PostScript name without a subset tag (e.g. <c>Arial-BoldMT</c>);
    /// empty for an image.</summary>
    public string FontName { get; internal set; } = string.Empty;

    /// <summary>The effective font size in points (the size scaled by the text and current
    /// matrices).</summary>
    public float FontSize { get; internal set; }

    /// <summary>Whether the text is bold: a bold face (by its name, weight or flags) or text
    /// filled and stroked to look bold.</summary>
    public bool IsBold { get; internal set; }

    /// <summary>Whether the text is italic or oblique (by its face's name, angle or flags).</summary>
    public bool IsItalic { get; internal set; }

    /// <summary>The colour the text is painted in; for a drawing, the colour all its paths are painted in (a stroked path's
    /// stroke, another's fill), null when they differ.</summary>
    public Color? ForegroundColor { get; internal set; }

    /// <summary>The area the item covers on its page as the page is shown (turned by its /Rotate, where
    /// text extraction places its text): the run's baseline span from its descent to its ascent,
    /// or the image's placement.</summary>
    public Rectangle Rectangle { get; internal set; } = new(0, 0, 0, 0);

    /// <summary>The direction of the run's baseline on the page as it is shown, in whole degrees counter-clockwise
    /// (0: left to right; 90: running up the page); <see cref="Rectangle"/> is the box a turned run covers. 0 for an
    /// image.</summary>
    public int Rotation { get; internal set; }

    /// <summary>The image; null for a text run.</summary>
    public XImage? Image { get; internal set; }

    /// <summary>For an image, the matrix its unit square is drawn through onto the page as it is shown.</summary>
    internal double[]? ImageMatrix { get; set; }

    /// <summary>Writes the image as it appears on the page as it is shown: turned and mirrored as
    /// its placement and the page's /Rotate draw it (<see cref="Image"/> holds it as stored, which a
    /// page may draw turned or flipped). An image drawn as stored is written verbatim (a JPEG stays
    /// the same JPEG); otherwise a JPEG is written as JPEG and any other image as PNG.</summary>
    /// <exception cref="InvalidOperationException">The item is not an image.</exception>
    public void SaveImage(Stream output)
    {
        if (Image is null) throw new InvalidOperationException("The item is not an image.");
        var m = ImageMatrix;
        // The image's columns run along the unit square's x axis (m[0], m[1]); its rows run from
        // the top, the square's y = 1 edge, down its y axis (-m[2], -m[3]). The output grid's y points down.
        var across = m is null ? null : GridStep(m[0], -m[1]);
        var down = m is null ? null : GridStep(-m[2], m[3]);
        // As stored when drawn so - or skewed, degenerate or of unknown placement.
        if (across is null || down is null || (across.Value.X == 0) == (down.Value.X == 0)
            || (across.Value == (1, 0) && down.Value == (0, 1)))
        {
            if (Image.IsJpeg) Image.Save(output);
            else output.Write(Image.ToPng());
            return;
        }
        Image.SaveOriented(output, across.Value, down.Value);
    }

    /// <summary>The unit grid step a direction runs nearest; null for a zero direction.</summary>
    private static (int X, int Y)? GridStep(double x, double y)
    {
        if (x == 0 && y == 0) return null;
        return Math.Abs(x) >= Math.Abs(y) ? (Math.Sign(x), 0) : (0, Math.Sign(y));
    }

    /// <summary>For an artifact's content, the artifact's /Type (Undefined when it states
    /// none); null for content a structure element marks.</summary>
    public Artifact.ArtifactType? ArtifactType { get; internal set; }

    /// <summary>For an artifact's content, the artifact's /Subtype (Header, Footer, Watermark
    /// ...; Undefined when it states none); null for content a structure element marks.</summary>
    public Artifact.ArtifactSubtype? ArtifactSubtype { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() => Kind == MarkedContentKind.Image ? $"[image {Image?.Name}]" : Text;
}
