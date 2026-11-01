using System.Globalization;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The band of a page its content occupies, in the text layer's em grid from the page
    /// top: what <see cref="HtmlSaveOptions.RemoveEmptyAreasOnTopAndBottom"/> keeps of the page.</summary>
    private readonly struct ContentBand
    {
        public ContentBand(double topEm, double bottomEm)
        {
            TopEm = topEm;
            BottomEm = bottomEm;
        }

        public double TopEm { get; }
        public double BottomEm { get; }
        public double HeightEm => BottomEm - TopEm;
    }

    /// <summary>Rounding slack when a page height in em is taken back to whole points.</summary>
    private const double PageHeightSlackPt = 1e-6;

    /// <summary>The line height a run takes when its face states none.</summary>
    private const double FallbackLineHeightEm = 1.2;

    /// <summary>The value the line-height class of a run carries: its face's line height, or the fallback.</summary>
    private static double LineHeightClassEm(double lineHeightEm) =>
        lineHeightEm > 0 ? Math.Round(lineHeightEm, 6) : FallbackLineHeightEm;

    /// <summary>
    /// The band a page keeps when the empty areas above and below its content are removed, or null
    /// when the option is off or the page is turned (its text grid is not the page's, and the band of
    /// a turned page is unmeasured). The band runs from the topmost content - a line's box top or a
    /// graphic's ink - to the lowest: a graphic's ink, or a line's baseline plus its line height, which
    /// is one ascent under the line's box. A page with no text keeps its top; a page with nothing on it
    /// keeps nothing. No margin is added.
    /// </summary>
    private static ContentBand? TrimBand(ExternalDivState xd)
    {
        if (xd.options?.RemoveEmptyAreasOnTopAndBottom != true || xd.model.PageExtentEm is not { } page) return null;
        var m = xd.model;
        var hasInk = m.InkTopEm <= m.InkBottomEm;
        var top = m.HasText ? Math.Min(m.TextTopEm, hasInk ? m.InkTopEm : m.TextTopEm) : 0;
        var bottom = Math.Max(m.HasText ? m.TextBottomEm : 0, hasInk ? m.InkBottomEm : 0);
        // The band ends where the untrimmed page box does: at its whole points, the height the page
        // div states, so content reaching the page's foot leaves the page untrimmed.
        var height = Math.Floor(page.Height * 12.0 + PageHeightSlackPt) / 12.0;
        top = Compat.Clamp(top, 0, height);
        return new ContentBand(top, Compat.Clamp(bottom, top, height));
    }

    /// <summary>The path about to be painted is ink of the page: its box, widened by half the stroke
    /// width when stroked and cut to a rectangular clip in force, extends the page's content band.</summary>
    private static void RecordPathInk(ContentRenderState ct, bool stroked)
    {
        if (ct.model is null || ct.pathState.MinX > ct.pathState.MaxX) return;
        var half = stroked ? ct.pathState.LineWidth / 2 : 0;
        RecordInkBox(ct, ct.pathState.MinX - half, ct.pathState.MinY - half, ct.pathState.MaxX + half, ct.pathState.MaxY + half);
    }

    /// <summary>An image is ink over the unit square its transform places.</summary>
    private static void RecordImageInk(ContentRenderState ct)
    {
        if (ct.model is null || ct.operands.Count < 1 || ct.operands[0] is not PdfName name
            || !ct.imageXObjects.ContainsKey(name.Value)) return;
        var (ax, ay) = Dp(ct, 0, 0);
        var (bx, by) = Dp(ct, 1, 0);
        var (cx, cy) = Dp(ct, 0, 1);
        var (dx, dy) = Dp(ct, 1, 1);
        RecordInkBox(ct, Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)), Math.Min(Math.Min(ay, by), Math.Min(cy, dy)),
            Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)), Math.Max(Math.Max(ay, by), Math.Max(cy, dy)));
    }

    /// <summary>A shading painted onto the page is ink over the clip in force, or the whole page.</summary>
    private static void RecordShadingInk(ContentRenderState ct)
    {
        if (ct.model is null || ct.operands.Count < 1 || ct.operands[0] is not PdfName) return;
        var yTop = double.IsNaN(ct.yTopRef) ? ct.pageHeight : ct.yTopRef;
        RecordInkBox(ct, double.NegativeInfinity, yTop - ct.pageHeight, double.PositiveInfinity, yTop);
    }

    /// <summary>A box of ink in page space (points, y up), cut to a rectangular clip in force, recorded
    /// on the text-layer model in its em grid from the page top.</summary>
    private static void RecordInkBox(ContentRenderState ct, double x0, double y0, double x1, double y1)
    {
        if (ct.clipD is { Length: > 0 } clip && TryRectOfPath(clip) is { } c)
        {
            x0 = Math.Max(x0, c.X0); y0 = Math.Max(y0, c.Y0); x1 = Math.Min(x1, c.X1); y1 = Math.Min(y1, c.Y1);
            if (x1 < x0 || y1 < y0) return;
        }
        var yTop = double.IsNaN(ct.yTopRef) ? ct.pageHeight : ct.yTopRef;
        ct.model!.RecordInk((yTop - y1) / 12.0, (yTop - y0) / 12.0);
    }

    /// <summary>A stylesheet length in em, printed as the structural classes print theirs.</summary>
    private static string EmG7(double v) => ((float)v).ToString("G7", CultureInfo.InvariantCulture) + "em";

    /// <summary>The page div's inline style under the trim: its own band height when it is not the one
    /// the stylesheet states, and a cut for a page raster, which is opaque to the page's full height
    /// where the page SVG is cut at the band instead.</summary>
    private static string PageDivStyleAttr(ExternalDivState xd)
    {
        var style = "";
        if (xd.band is { } band)
        {
            if (xd.styleReg.Band is not { } pinned || pinned.HeightEm != band.HeightEm) style += $"height:{EmG7(band.HeightEm)};";
            if (xd.emitPngBackground) style += "overflow:hidden;";
        }
        else if (xd.styleReg.Band is not null)
        {
            style = $"height:{EmG7(Math.Floor(xd.pageHeight) / 12.0)};";
        }
        return style.Length > 0 ? $" style=\"{style}\"" : "";
    }

    /// <summary>The backdrop holder's inline style under the trim: the page's own lift when it is not
    /// the one the stylesheet states.</summary>
    private static string HolderStyleAttr(ExternalDivState xd)
    {
        var pinnedTop = xd.styleReg.Band?.TopEm;
        var top = xd.band?.TopEm ?? (pinnedTop is null ? (double?)null : 0);
        return top is { } t && t != pinnedTop ? $" style=\"top:-{EmG7(t)};\"" : "";
    }
}
