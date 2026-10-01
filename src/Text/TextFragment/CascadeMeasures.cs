

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// Cascade re-flow helpers: line extents and the packed-word measures.
    // Greedy pack: first line from the match X, continuation lines from their own
    // ORIGINAL left margin (hanging-indent items keep the continuation indent);
    // lines created beyond the paragraph continue at the last line's indent.
    private static double LxAt(CascadeState cf, int i2)
    {
        int li2 = cf.matchLine + i2;
        return li2 < cf.paraLines.Count ? cf.paraLines[li2].lx : cf.paraLines[^1].lx;
    }

    private static double MixedW(CascadeState cf, string w)
    {
        double t = 0;
        for (var mi = 0; mi < w.Length; mi++)
        {
            var mc = w[mi];
            if (mc < 0x100)
            {
                t += cf.effFs * 0.5;   // half-width Latin cell of the CJK face
                continue;
            }
            if (char.IsHighSurrogate(mc) && mi + 1 < w.Length && char.IsLowSurrogate(w[mi + 1]))
            {
                mi++;
                t += cf.effFs;   // a supplementary CJK glyph advances a full em
                continue;
            }
            t += cf.effFs;       // BMP CJK: full-width cell
        }
        return t;
    }

    private static double SpaceW(CascadeState cf)
    {
        if (cf.cjkBase) return cf.effFs * 0.5;   // the CJK face's half-width space cell
        try
        {
            return cf.degenerateMetrics
                ? cf.font!.SourceFontData!.MeasureString(" ", cf.effFs)
                : cf.font!.MeasureString(" ", cf.effFs);
        }
        catch { return cf.effFs * 0.25; }
    }

    private static double WordW(CascadeState cf, string w)
    {
        if (cf.cjkBase) return MixedW(cf, w);
        try
        {
            return cf.degenerateMetrics
                ? cf.font!.SourceFontData!.MeasureString(w, cf.effFs)
                : cf.font!.MeasureString(w, cf.effFs);
        }
        catch { return w.Length * cf.effFs * 0.5; }
    }

    // The SOURCE line's own right extent, for the seam-space rule below; lines
    // beyond the grid read the paragraph width.
    private static double RxAt(CascadeState cf, int i2)
    {
        int li2 = cf.matchLine + i2;
        return li2 < cf.paraLines.Count ? cf.paraLines[li2].rx : cf.rightX;
    }
}
