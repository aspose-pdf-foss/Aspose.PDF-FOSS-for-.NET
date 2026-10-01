using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the styled-data leaf layout: the glyph advance and one wrapped line at a time.

    /// <summary>The advance of one character in the leaf face, in points.</summary>
    private static double AdvW(StyledDataDocState sy, Text.GlyphOutlineParser gp, (double winAsc, double winDesc, double upm) fm, char c) =>
        (gp.CMap.TryGetValue(c, out var g) ? gp.GetAdvanceWidth(g) : 0) * sy.size / fm.upm;

    /// <summary>x</summary>
    private static void LayoutStyledDataLine(StyledDataDocState sy, StyledNode p, int li, (double winAsc, double winDesc, double upm) fm, Text.GlyphOutlineParser gp)
    {
        var ln = sy.lines[li];
        var x = sy.x0;
        var gi = 0;
        while (gi < ln.Count)
        {
            var runIdx = ln[gi].run;
            var piece = new StringBuilder();
            var pieceW = 0.0;
            while (gi < ln.Count && ln[gi].run == runIdx)
            {
                piece.Append(ln[gi].c);
                pieceW += AdvW(sy, gp, fm, ln[gi].c) + sy.lsF;
                gi++;
            }
            sy.runsOut.Add((sy.y, x, piece.ToString(), p));
            // The trailing letter-spacing advances the next run's X but does
            // not extend the drawn extent of this one.
            var urx = x + pieceW - (sy.lsF > 0 ? sy.lsF : 0);
            if (urx > sy.maxUrx) sy.maxUrx = urx;
            x += pieceW;
        }
        if (li < sy.lines.Count - 1) sy.y += sy.lh;
    }
}
