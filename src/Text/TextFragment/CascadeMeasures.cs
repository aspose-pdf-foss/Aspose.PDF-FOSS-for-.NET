

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// Cascade re-flow helpers: line extents, the word gaps between a line's runs and the packed-word measures.
    /// <summary>The segments of a line that its text reads a space before. A producer that draws a line word by
    /// word shows no space glyph between its runs; the absorber read the gaps into the line's text, and that is
    /// where the runs part when a re-flow joins them again.</summary>
    private static System.Collections.Generic.HashSet<TextSegment> GapsBefore(TextFragment line)
    {
        var gaps = new System.Collections.Generic.HashSet<TextSegment>(ReferenceEqualityComparer.Instance);
        var text = line.Text;
        var cursor = 0;
        foreach (var seg in line.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var at = text.IndexOf(seg.Text, cursor, System.StringComparison.Ordinal);
            if (at < 0) break;
            if (at > cursor && text[at - 1] == ' ') gaps.Add(seg);
            cursor = at + seg.Text.Length;
        }
        return gaps;
    }

    // Greedy pack: first line from the match X, continuation lines from their own
    // ORIGINAL left margin (hanging-indent items keep the continuation indent); lines
    // created beyond the paragraph continue at the last line's indent.
    private static double LxAt(CascadeState cf, int i2)
    {
        int li2 = cf.matchLine + i2;
        return li2 < cf.paraLines.Count ? cf.paraLines[li2].lx : cf.paraLines[^1].lx;
    }

    private static double MixedW(CascadeState cf, string w)
    {
        double t = 0;
        var mi = 0;
        while (mi < w.Length)
        {
            var mc = w[mi];
            if (mc < 0x100)
            {
                t += cf.effFs * 0.5;   // half-width Latin cell of the CJK face
                mi++;
                continue;
            }
            if (char.IsHighSurrogate(mc) && mi + 1 < w.Length && char.IsLowSurrogate(w[mi + 1]))
            {
                mi++;
                t += cf.effFs;   // a supplementary CJK glyph advances a full em
                mi++;
                continue;
            }
            t += cf.effFs;       // BMP CJK: full-width cell
            mi++;
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
