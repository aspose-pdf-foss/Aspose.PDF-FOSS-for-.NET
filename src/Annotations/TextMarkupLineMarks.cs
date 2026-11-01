namespace Aspose.Pdf.Annotations;

/// <summary>The line marks of underline and squiggly annotations, drawn as a viewer draws them for an annotation
/// stored without an appearance.</summary>
public partial class TextMarkupAnnotation
{
    // A line mark is a fourteenth of the marked line's height thick, never thinner than a point.
    private const double MarkWidthOfLineHeight = 1.0 / 14;
    private const double MinMarkWidth = 1;

    /// <summary>Paint one mark per /QuadPoints quad - or one for the rectangle when there are none - as the
    /// normal appearance; <paramref name="mark"/> lays the path of a mark in a quad's bounds at a stroke width.</summary>
    private protected void DrawLineMarks(Action<Content.ContentStreamBuilder, (double minX, double minY, double maxX, double maxY), double> mark)
    {
        var r = Rect;
        if (r is null) return;
        var quads = QuadPoints;
        var boxes = new List<(double minX, double minY, double maxX, double maxY)>();
        for (var i = 0; i + 3 < quads.Length; i += 4) boxes.Add(QuadBounds(quads, i));
        if (boxes.Count == 0) boxes.Add((r.LLX, r.LLY, r.URX, r.URY));

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        foreach (var box in boxes)
        {
            var width = Math.Max(MinMarkWidth, (box.maxY - box.minY) * MarkWidthOfLineHeight);
            b.SetLineWidth(width);
            mark(b, box, width);
            b.Stroke();
        }
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }
}
