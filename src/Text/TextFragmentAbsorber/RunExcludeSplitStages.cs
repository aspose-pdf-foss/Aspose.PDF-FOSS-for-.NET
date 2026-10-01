using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>The stages of the exclude-rectangle run split: one run.</summary>
    /// <returns>The index of the last run this call left in the list at <paramref name="ri"/>'s place - the run
    /// itself, the run before it when it was dropped whole, or the last of the pieces it split into.</returns>
    private static int SplitRunByExcludeRects(List<RawTextRun> runs, Rectangle[] excludeRects, int ri)
    {
        var rx = new RunExcludeSplitState();
        rx.runs = runs;
        rx.excludeRects = excludeRects;
        rx.run = rx.runs[ri];
        rx.n = rx.run.Text.Length;
        if (rx.n == 0 || rx.run.Text == "\r\n") return ri;

        rx.fs = rx.run.FontSize;
        rx.descentOffset = 0;
        rx.ascentHeight = rx.fs;
        if (rx.run.Metrics is not null && rx.run.Metrics.Descent != 0)
            rx.descentOffset = rx.run.Metrics.Descent * rx.fs / 1000.0;
        if (rx.run.Metrics is not null && rx.run.Metrics.Ascent > 0)
            rx.ascentHeight = rx.run.Metrics.Ascent * rx.fs / 1000.0;
        var (_, by1) = ApplyCtm(rx.run.X + rx.run.TmC * rx.descentOffset, rx.run.Y + rx.run.TmD * rx.descentOffset, rx.run.Ctm);
        var (_, by2) = ApplyCtm(rx.run.X + rx.run.TmC * rx.ascentHeight, rx.run.Y + rx.run.TmD * rx.ascentHeight, rx.run.Ctm);
        rx.bandLly = Math.Min(by1, by2);
        rx.bandUry = Math.Max(by1, by2);
        rx.bandH = rx.bandUry - rx.bandLly;

        rx.tol = 0.5;
        rx.dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_EXCLDEBUG") == "1";
        rx.active = new List<Rectangle>();
        foreach (var er in rx.excludeRects)
        {
            if (er is null || er.IsEmpty) continue;
            var overlapV = Math.Min(rx.bandUry, er.URY) - Math.Max(rx.bandLly, er.LLY);
            if (overlapV > rx.tol) rx.active.Add(er);
        }
        if (rx.dbg) Console.Error.WriteLine($"[excl] run '{(rx.run.Text.Length > 24 ? rx.run.Text.Substring(0, 24) : rx.run.Text)}' X={rx.run.X:0.#} Y={rx.run.Y:0.#} band={rx.bandLly:0.#}..{rx.bandUry:0.#} active={rx.active.Count} cum={(rx.run.CharCumWidths?.Length.ToString() ?? "null")} ends={(rx.run.CharEndPositions?.Length.ToString() ?? "null")} n={rx.n}");
        if (rx.active.Count == 0) return ri;

        rx.cum = rx.run.CharCumWidths;
        rx.ends = rx.run.CharEndPositions;
        if (rx.ends is not null && rx.ends.Length < rx.n) rx.ends = null;
        rx.charX = new double[rx.n + 1];
        rx.haveCum = rx.cum is not null && rx.cum.Length > rx.n;
        ReadRunGlyphAdvances(rx);

        rx.excluded = new bool[rx.n];
        rx.any = false;
        MarkExcludedGlyphs(rx);
        if (!rx.any) return ri;

        // Without per-char advances the pieces cannot be re-based; drop the run
        // only when everything is excluded, otherwise keep it whole.
        if (!rx.haveCum)
        {
            var all = true;
            for (var i = 0; i < rx.n; i++) if (!rx.excluded[i]) { all = false; break; }
            if (all) { rx.runs.RemoveAt(ri); return ri - 1; }
            return ri;
        }

        rx.pieces = new List<RawTextRun>();
        rx.start = -1;
        EmitExcludeSplitPieces(rx);
        rx.runs.RemoveAt(ri);
        rx.runs.InsertRange(ri, rx.pieces);
        return ri + rx.pieces.Count - 1;
    }

    /// <summary></summary>
    private static void EmitExcludeSplitPieces(RunExcludeSplitState rx)
    {
        for (var i = 0; i <= rx.n; i++)
        {
            var keep = i < rx.n && !rx.excluded[i];
            if (keep && rx.start < 0) rx.start = i;
            if (!keep && rx.start >= 0)
            {
                var len = i - rx.start;
                var baseAdv = rx.cum![rx.start];
                var subCum = new double[len];
                var subEnds = new double[len];
                for (var k = 0; k < len; k++)
                {
                    subCum[k] = rx.cum[rx.start + k] - baseAdv;
                    // No recorded ink ends: the next char's start is the end.
                    subEnds[k] = rx.ends is not null
                        ? rx.ends[rx.start + k] - baseAdv
                        : rx.cum[rx.start + k + 1] - baseAdv;
                }
                var lastPad = rx.run.CharSpacing
                    + (rx.run.Text[i - 1] == ' ' ? rx.run.WordSpacing : 0.0);
                var pieceWidth = Math.Max(0, subEnds[len - 1] - lastPad);
                subEnds[len - 1] = pieceWidth;
                rx.pieces.Add(rx.run with
                {
                    Text = rx.run.Text.Substring(rx.start, len),
                    X = rx.run.X + rx.run.TmA * baseAdv,
                    Y = rx.run.Y + rx.run.TmB * baseAdv,
                    Width = pieceWidth,
                    CharCumWidths = subCum,
                    CharEndPositions = rx.ends is not null ? subEnds : null,
                    GapSplit = rx.start > 0,
                });
                rx.start = -1;
            }
        }
    }

    /// <summary></summary>
    private static void MarkExcludedGlyphs(RunExcludeSplitState rx)
    {
        for (var i = 0; i < rx.n; i++)
        {
            // A character belongs to the excluded area when its END lands
            // inside the rectangle — a glyph merely straddling the RIGHT edge
            // stays with the text after the area, while one straddling the
            // left edge (its end inside) is consumed by the area.
            var cend = Math.Max(rx.charX[i], rx.charX[i + 1]);
            foreach (var er in rx.active)
            {
                if (cend > er.LLX + rx.tol && cend <= er.URX + rx.tol) { rx.excluded[i] = true; rx.any = true; break; }
            }
        }
    }

    /// <summary></summary>
    private static void ReadRunGlyphAdvances(RunExcludeSplitState rx)
    {
        if (rx.cum is not null && rx.cum.Length > rx.n)
        {
            for (var i = 0; i <= rx.n; i++)
            {
                var (px, _) = ApplyCtm(rx.run.X + rx.run.TmA * rx.cum[i] * rx.run.HScaling,
                    rx.run.Y + rx.run.TmB * rx.cum[i] * rx.run.HScaling, rx.run.Ctm);
                rx.charX[i] = px;
            }
        }
        else
        {
            var totalW = rx.run.Width > 0 ? rx.run.Width : EstimateWidth(rx.run.Text, rx.fs);
            for (var i = 0; i <= rx.n; i++)
            {
                var cw = totalW * i / rx.n;
                var (px, _) = ApplyCtm(rx.run.X + rx.run.TmA * cw * rx.run.HScaling,
                    rx.run.Y + rx.run.TmB * cw * rx.run.HScaling, rx.run.Ctm);
                rx.charX[i] = px;
            }
        }
    }
}
