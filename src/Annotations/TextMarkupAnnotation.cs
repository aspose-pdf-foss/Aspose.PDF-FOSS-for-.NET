using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Base class for the text-markup annotations (Highlight, Underline, StrikeOut,
/// Squiggly) — those whose geometry is a set of QuadPoints over page text. Adds
/// the ability to recover the text the markup covers.
/// </summary>
public partial class TextMarkupAnnotation : MarkupAnnotation
{
    internal TextMarkupAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected TextMarkupAnnotation(Page page, Rectangle rect) : base(page, rect) { }
    protected TextMarkupAnnotation(Document document, Rectangle rect) : base(document, rect) { }
    /// <summary>Document-bound ctor (added to a page later).</summary>
    public TextMarkupAnnotation(Document document) : base(document) { }

    /// <summary>The text covered by this annotation's QuadPoints, with the text of
    /// each quad (each highlighted line) separated by <see cref="System.Environment.NewLine"/>.</summary>
    public string GetMarkedText()
    {
        if (Page is not { } page || QuadPoints is not { Length: >= 4 } quads)
            return string.Empty;
        var fragments = AbsorbFragments(page);
        var parts = new System.Collections.Generic.List<string>();
        for (var i = 0; i + 3 < quads.Length; i += 4)
        {
            var (minX, minY, maxX, maxY) = QuadBounds(quads, i);
            var sb = new System.Text.StringBuilder();
            foreach (var f in fragments)
                CollectChars(f, minX, minY, maxX, maxY, sb, null);
            parts.Add(sb.ToString());
        }
        return string.Join(System.Environment.NewLine, parts);
    }

    /// <summary>The page text fragments covered by this annotation's QuadPoints —
    /// one clipped fragment per source fragment that each quad overlaps.</summary>
    public Aspose.Pdf.Text.TextFragmentCollection GetMarkedTextFragments()
    {
        var mt = new MarkedTextState();
        mt.result = new Aspose.Pdf.Text.TextFragmentCollection();
        if (Page is not { } page || QuadPoints is not { Length: >= 4 } quads)
            return mt.result;
        mt.fragments = AbsorbFragments(page);

        mt.boxes = new System.Collections.Generic.List<(double minX, double minY, double maxX, double maxY)>();
        for (var i = 0; i + 3 < quads.Length; i += 4) mt.boxes.Add(QuadBounds(quads, i));
        if (mt.boxes.Count == 0) return mt.result;

        mt.grazeTolerance = 0.1;
        mt.groups = new System.Collections.Generic.Dictionary<(int q, int fi),
            System.Collections.Generic.List<(char ch, double cx)>>();
        mt.order = new System.Collections.Generic.List<(int q, int fi)>();
        mt.fi = 0;
        foreach (var f in mt.fragments)
        {
            MatchFragmentToQuads(mt, f);
        }

        mt.pieces = new System.Collections.Generic.List<(double midY, double minX, double maxX, string text)>();
        mt.rightMargin = double.MinValue;
        foreach (var key in mt.order)
        {
            CollectMarkedLineKey(mt, key);
        }
        if (mt.pieces.Count == 0) return mt.result;

        // Reading order: group pieces into lines by vertical centre (top first), then
        // order each line left-to-right — /QuadPoints order need not be the visual order.
        mt.pieces.Sort((a, b) => b.midY.CompareTo(a.midY));
        mt.lines = new System.Collections.Generic.List<System.Collections.Generic.List<(double midY, double minX, double maxX, string text)>>();
        mt.lineTol = 6.0;
        foreach (var p in mt.pieces)
        {
            if (mt.lines.Count > 0 && System.Math.Abs(mt.lines[^1][0].midY - p.midY) <= mt.lineTol)
                mt.lines[^1].Add(p);
            else
                mt.lines.Add(new System.Collections.Generic.List<(double, double, double, string)> { p });
        }

        for (var li = 0; li < mt.lines.Count; li++)
        {
            EmitMarkedLine(mt, li);
        }
        return mt.result;
    }

    private static System.Collections.Generic.List<Aspose.Pdf.Text.TextFragment> AbsorbFragments(Page page)
    {
        var absorber = new Aspose.Pdf.Text.TextFragmentAbsorber();
        page.Accept(absorber);
        var list = new System.Collections.Generic.List<Aspose.Pdf.Text.TextFragment>();
        foreach (Aspose.Pdf.Text.TextFragment f in absorber.TextFragments) list.Add(f);
        return list;
    }

    private static (double minX, double minY, double maxX, double maxY) QuadBounds(Point[] q, int i)
    {
        double minX = Math.Min(Math.Min(q[i].X, q[i + 1].X), Math.Min(q[i + 2].X, q[i + 3].X));
        double maxX = Math.Max(Math.Max(q[i].X, q[i + 1].X), Math.Max(q[i + 2].X, q[i + 3].X));
        double minY = Math.Min(Math.Min(q[i].Y, q[i + 1].Y), Math.Min(q[i + 2].Y, q[i + 3].Y));
        double maxY = Math.Max(Math.Max(q[i].Y, q[i + 1].Y), Math.Max(q[i + 2].Y, q[i + 3].Y));
        return (minX, minY, maxX, maxY);
    }

    /// <summary>Append the characters of <paramref name="fragment"/> that the quad
    /// box covers to <paramref name="sb"/>. A character counts as marked when its
    /// glyph box overlaps the quad's X range by more than a small grazing tolerance
    /// (so a glyph that only just touches the boundary is excluded) and its vertical
    /// centre is within the quad's Y band.</summary>
    private static void CollectChars(Aspose.Pdf.Text.TextFragment fragment,
        double minX, double minY, double maxX, double maxY, System.Text.StringBuilder sb, object? _)
    {
        const double grazeTolerance = 0.1; // points: ignore sub-0.1pt boundary overlaps
        foreach (Aspose.Pdf.Text.TextSegment seg in fragment.Segments)
        {
            var chars = seg.Characters;
            var text = seg.Text ?? string.Empty;
            for (var c = 1; c <= chars.Count && c <= text.Length; c++)
            {
                var r = chars[c].Rectangle;
                var overlapX = Math.Min(r.URX, maxX) - Math.Max(r.LLX, minX);
                var cy = (r.LLY + r.URY) / 2.0;
                if (overlapX > grazeTolerance && cy >= minY - 2 && cy <= maxY + 2)
                    sb.Append(text[c - 1]);
            }
        }
    }
}
