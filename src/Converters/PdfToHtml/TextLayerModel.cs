using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One box of a page's text layer in the layer's em grid (the left/top a line div is
    /// positioned with, its right and bottom edges) and the paint ordinal it was drawn at.</summary>
    private readonly struct LineBox
    {
        public LineBox(double l, double t, double r, double b, int seq)
        {
            L = l; T = t; R = Math.Max(r, l + MinBoxExtentEm); B = Math.Max(b, t + MinBoxExtentEm); Seq = seq;
        }

        public double L { get; }
        public double T { get; }
        public double R { get; }
        public double B { get; }
        public int Seq { get; }

        /// <summary>True when this box lies inside <paramref name="cover"/>, a little slack allowed.</summary>
        public bool Inside(LineBox cover) =>
            cover.L <= L + CoverSlackEm && cover.R >= R - CoverSlackEm
            && cover.T <= T + CoverSlackEm && cover.B >= B - CoverSlackEm;
    }

    /// <summary>A box of no measurable extent still occupies a sliver, so gap and cover tests stay defined.</summary>
    private const double MinBoxExtentEm = 0.01;

    /// <summary>How far a line may poke out of an opaque fill and still count as painted over.</summary>
    private const double CoverSlackEm = 0.1;

    /// <summary>
    /// What the content render learns about a page's text layer beyond the markup it writes: the
    /// box of every line div in emission order with the paint ordinal of the line's first show,
    /// every opaque rectangle the content fills with its own ordinal, and the optional-content
    /// groups the document's default configuration hides. The layer reads it back once, when
    /// its line divs are put in reading order: a line a later fill paints over is dropped, and the
    /// lines that remain are grouped into column blocks before they are sorted by baseline.
    /// </summary>
    private sealed class TextLayerModel
    {
        public List<LineBox> Boxes { get; } = new();
        public List<LineBox> Covers { get; } = new();

        /// <summary>Running paint ordinal over the page's content and the forms it invokes.</summary>
        public int Seq { get; set; }

        /// <summary>The page box in the em grid, from its top-left corner; unset for a page the
        /// grid does not describe (a quarter-turned or turned-over one), where nothing is off-page.</summary>
        public (double Width, double Height)? PageExtentEm { get; set; }

        /// <summary>The band the page's text occupies, in the em grid from the page top: the topmost
        /// line div's top, and the lowest line's baseline plus its line height. Empty until a line is
        /// recorded.</summary>
        public double TextTopEm { get; private set; } = double.PositiveInfinity;

        public double TextBottomEm { get; private set; } = double.NegativeInfinity;

        /// <summary>The band the page's graphics ink occupies - paths, images, shadings - in the same grid.</summary>
        public double InkTopEm { get; private set; } = double.PositiveInfinity;

        public double InkBottomEm { get; private set; } = double.NegativeInfinity;

        public bool HasText => TextTopEm <= TextBottomEm;

        public void RecordText(double topEm, double bottomEm)
        {
            TextTopEm = Math.Min(TextTopEm, topEm);
            TextBottomEm = Math.Max(TextBottomEm, bottomEm);
        }

        public void RecordInk(double topEm, double bottomEm)
        {
            InkTopEm = Math.Min(InkTopEm, topEm);
            InkBottomEm = Math.Max(InkBottomEm, bottomEm);
        }

        /// <summary>True when the box lies wholly outside the page box.</summary>
        public bool OffPage(LineBox box) =>
            PageExtentEm is { } page && (box.R <= 0 || box.B <= 0 || box.L >= page.Width || box.T >= page.Height);

        /// <summary>True when a later opaque fill covers the whole box.</summary>
        public bool Covered(LineBox box)
        {
            foreach (var cover in Covers)
                if (cover.Seq > box.Seq && box.Inside(cover)) return true;
            return false;
        }
    }

    /// <summary>An opaque fill of one rectangle is recorded as a cover of the text layer with its
    /// paint ordinal: text drawn under it before it is hidden. A fill that is translucent,
    /// blended, soft-masked or clipped by anything but a rectangle has a footprint the
    /// rectangle does not describe, and is not a cover.</summary>
    private static void RecordOpaqueCover(ContentRenderState ct)
    {
        if (ct.model is null || ct.singleRectPathLength < 0 || ct.pathState.Data.Length != ct.singleRectPathLength) return;
        if (ct.opacityGroupOpen || ct.maskGroupOpen || !BlendPaintsOver(ct.blendMode)) return;
        var (x0, y0, x1, y1) = ct.singleRectBox;
        if (ct.clipD is { Length: > 0 } clip)
        {
            if (TryRectOfPath(clip) is not { } c) return;
            x0 = Math.Max(x0, c.X0); y0 = Math.Max(y0, c.Y0); x1 = Math.Min(x1, c.X1); y1 = Math.Min(y1, c.Y1);
            if (x1 <= x0 || y1 <= y0) return;
        }
        ct.model.Covers.Add(LayerBox(ct, x0, y1, x1, y0, ct.paintSeq));
    }

    /// <summary>An opaque image drawn upright is recorded as a cover of the text layer like an opaque fill: a
    /// scan painted over the text recognised from it hides that text. An image that is masked, soft-masked,
    /// translucent, blended or turned has a footprint its box does not describe, and is not a cover.</summary>
    private static void RecordOpaqueImageCover(ContentRenderState ct)
    {
        if (ct.model is null || ct.operands.Count < 1 || ct.operands[0] is not PdfName name
            || !ct.imageXObjects.TryGetValue(name.Value, out var image)) return;
        if (ct.opacityGroupOpen || ct.maskGroupOpen || !BlendPaintsOver(ct.blendMode)) return;
        var dict = image.Stream.Dict;
        if (dict.ContainsKey("SMask") || dict.ContainsKey("Mask") || dict.Get("ImageMask") is PdfBoolean { Value: true }
            || dict.Get("SMaskInData") is PdfInteger { Value: > 0 }) return;
        if (Math.Abs(ct.ctm.B) > UprightTolerance || Math.Abs(ct.ctm.C) > UprightTolerance) return;
        var (ax, ay) = Dp(ct, 0, 0);
        var (bx, by) = Dp(ct, 1, 1);
        double x0 = Math.Min(ax, bx), y0 = Math.Min(ay, by), x1 = Math.Max(ax, bx), y1 = Math.Max(ay, by);
        if (ct.clipD is { Length: > 0 } clip)
        {
            if (TryRectOfPath(clip) is not { } c) return;
            x0 = Math.Max(x0, c.X0); y0 = Math.Max(y0, c.Y0); x1 = Math.Min(x1, c.X1); y1 = Math.Min(y1, c.Y1);
            if (x1 <= x0 || y1 <= y0) return;
        }
        ct.model.Covers.Add(LayerBox(ct, x0, y1, x1, y0, ct.paintSeq));
    }

    /// <summary>A transform term this small leaves an image upright.</summary>
    private const double UprightTolerance = 1e-6;

    /// <summary>The box of a path that is one rectangle (four corners), else null.</summary>
    private static (double X0, double Y0, double X1, double Y1)? TryRectOfPath(string d)
    {
        var parts = d.Replace("M", " ").Replace("L", " ").Replace("Z", " ")
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 8) return null;
        var v = new double[8];
        for (var i = 0; i < 8; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                return null;
        return (Math.Min(Math.Min(v[0], v[2]), Math.Min(v[4], v[6])), Math.Min(Math.Min(v[1], v[3]), Math.Min(v[5], v[7])),
            Math.Max(Math.Max(v[0], v[2]), Math.Max(v[4], v[6])), Math.Max(Math.Max(v[1], v[3]), Math.Max(v[5], v[7])));
    }

    /// <summary>A device-space box (PDF points, y up) in the text layer's em grid: the grid the
    /// line divs are positioned in, shifted like them when the page is turned over.</summary>
    private static LineBox LayerBox(ContentRenderState ct, double left, double topY, double right, double bottomY, int seq)
    {
        var yTop = double.IsNaN(ct.yTopRef) ? ct.pageHeight : ct.yTopRef;
        var shiftL = ct.pageTurnedOver ? ct.pageWidth / 12.0 : 0;
        var shiftT = ct.pageTurnedOver ? ct.pageHeight / 12.0 : 0;
        return new LineBox((left - ct.pageLLX) / 12.0 - shiftL, (yTop - topY) / 12.0 - shiftT,
            (right - ct.pageLLX) / 12.0 - shiftL, (yTop - bottomY) / 12.0 - shiftT, seq);
    }

    /// <summary>A column gap narrower than this is a word gap or an indent, not a gutter between blocks.</summary>
    private const double MinColumnGutterEm = 0.5;

    /// <summary>A vertical gap must at least clear rounding to separate two blocks.</summary>
    private const double MinRowGapEm = 0.01;

    /// <summary>
    /// Reading order of a run's line divs from their boxes: a set of lines that a clear vertical
    /// gutter divides falls into columns, read left to right; otherwise the widest horizontal gap
    /// divides it into an upper and a lower block, read top to bottom; a set with neither reads
    /// by top, then left. Two paragraph columns therefore read one after the other, a line that
    /// spans both columns reads after them, and a table's cells stay column by column.
    /// </summary>
    private static void EmitBlockOrder(List<(double L, double T, string Html)> run, List<LineBox> boxes,
        List<int> idx, List<string> order)
    {
        if (idx.Count <= 1)
        {
            foreach (var i in idx) order.Add(run[i].Html);
            return;
        }
        var columns = new List<List<int>>();
        var maxR = double.MinValue;
        foreach (var i in idx.OrderBy(k => boxes[k].L))
        {
            if (columns.Count == 0 || boxes[i].L - maxR >= MinColumnGutterEm) columns.Add(new List<int>());
            columns[^1].Add(i);
            maxR = Math.Max(maxR, boxes[i].R);
        }
        if (columns.Count > 1)
        {
            foreach (var column in columns) EmitBlockOrder(run, boxes, column, order);
            return;
        }
        var byTop = idx.OrderBy(k => boxes[k].T).ThenBy(k => boxes[k].L).ToList();
        var maxB = boxes[byTop[0]].B;
        var cut = -1;
        var widest = MinRowGapEm;
        for (var k = 1; k < byTop.Count; k++)
        {
            var gap = boxes[byTop[k]].T - maxB;
            if (gap > widest) { widest = gap; cut = k; }
            maxB = Math.Max(maxB, boxes[byTop[k]].B);
        }
        if (cut > 0)
        {
            EmitBlockOrder(run, boxes, byTop.GetRange(0, cut), order);
            EmitBlockOrder(run, boxes, byTop.GetRange(cut, byTop.Count - cut), order);
            return;
        }
        foreach (var i in byTop) order.Add(run[i].Html);
    }
}
