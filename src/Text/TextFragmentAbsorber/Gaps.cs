using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>
    /// Flags runs whose immediately-preceding horizontal gap is part of a uniformly
    /// letter-tracked same-line sequence (constant inter-glyph advance), as opposed to a
    /// genuine word break. Letterhead/display text is often drawn with a fixed tracking
    /// that exceeds a normal word space, so a per-gap size threshold cannot tell it from a
    /// word gap — but its hallmark is that EVERY adjacent gap on the line is (near) equal.
    /// A run is flagged only inside a window of ≥3 consecutive near-equal, positive,
    /// sub-word-sized gaps. Word boundaries on such lines are explicit space glyphs, which
    /// are still appended, so suppressing gap-spaces here keeps words intact without merging.
    /// </summary>
    /// <summary>
    /// Flags runs that start PART-WAY THROUGH the previous run's advance — past its
    /// origin, but short of where it ends — in a way that reads as a token boundary.
    /// Japanese full-width punctuation is the everyday case: '）' and '、' carry a
    /// full-em advance but are set half-width, so the next glyph lands half an em early
    /// and a space is reported there, the pen having deviated from a flush
    /// advance by as much as a word gap does.
    /// <para>
    /// ★ Only an ISOLATED squeeze counts. A line drawn glyph by glyph at a uniformly
    /// tight step overlaps at EVERY pair, and that is how the line was set, not a token
    /// boundary — spacing those turns one word into one fragment per letter. So a gap
    /// whose same-line neighbour on either side is squeezed the same way is left alone,
    /// the same reasoning <see cref="ComputeLetterTrackedGaps"/> applies to uniform
    /// positive gaps.
    /// </para>
    /// Two neighbours this never claims: a doubled draw (a drop shadow re-starts AT the
    /// previous origin) and a back-jump (it starts left of it). Rotated runs are excluded
    /// outright — they advance along Y, so an X-gap between glyphs of one word is an
    /// artifact of the rotation and reads negative.
    /// </summary>
    private static bool[] ComputeSqueezedGaps(List<RawTextRun> runs)
    {
        var raw = new bool[runs.Count];
        // Index of the previous CONTENT run, so the neighbour test walks real glyphs and
        // steps over the \r\n sentinels that BT/ET boundaries inject mid-line.
        var prevOf = new int[runs.Count];
        var lastContent = -1;
        for (var i = 0; i < runs.Count; i++)
        {
            prevOf[i] = -1;
            if (runs[i].Text == "\r\n") continue;
            prevOf[i] = lastContent;
            lastContent = i;
            var p = prevOf[i];
            if (p < 0) continue;

            var prev = runs[p];
            var cur = runs[i];
            if (Math.Abs(cur.TmB) > Math.Abs(cur.TmA) * 1e-3
                || Math.Abs(prev.TmB) > Math.Abs(prev.TmA) * 1e-3
                || !IsUprightCtm(cur) || !IsUprightCtm(prev)) continue;
            if (Math.Abs(cur.Y - prev.Y) >= 2.0) continue; // different line

            var prevTmScale = Math.Abs(prev.TmA) > 0 ? Math.Abs(prev.TmA) : 1.0;
            var prevAdvance = (prev.Width > 0
                ? prev.Width * prev.HScaling
                : EstimateWidth(prev.Text, prev.FontSize)) * prevTmScale;
            if (prevAdvance <= 0) continue;
            var overlap = prev.X + prevAdvance - cur.X;
            var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
            var effFontSize = fs * (Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 1.0);
            raw[i] = overlap > effFontSize * 0.2 && cur.X >= prev.X + prevAdvance * 0.25;
        }

        var isolated = new bool[runs.Count];
        // The NEXT content run, so "squeezed on both sides" can be asked of a gap.
        var nextOf = new int[runs.Count];
        var nextContent = -1;
        for (var i = runs.Count - 1; i >= 0; i--)
        {
            nextOf[i] = nextContent;
            if (runs[i].Text != "\r\n") nextContent = i;
        }
        for (var i = 0; i < runs.Count; i++)
        {
            if (!raw[i]) continue;
            var before = prevOf[i] >= 0 && raw[prevOf[i]];
            var after = nextOf[i] >= 0 && raw[nextOf[i]];
            isolated[i] = !before && !after;
        }
        return isolated;
    }

    /// <summary>Ideographs, kana and the CJK punctuation/fullwidth blocks — scripts whose
    /// glyphs stand alone rather than spelling a word out of letters.</summary>
    private static bool IsCjk(char c) =>
        (c >= '　' && c <= 'ヿ')     // CJK symbols & punctuation, hiragana, katakana
        || (c >= '㐀' && c <= '䶿')  // unified ideographs extension A
        || (c >= '一' && c <= '鿿')  // unified ideographs
        || (c >= '豈' && c <= '﫿')  // compatibility ideographs
        || (c >= '＀' && c <= '￯'); // halfwidth & fullwidth forms

    private static bool[] ComputeLetterTrackedGaps(List<RawTextRun> runs)
    {
        var lg = new LetterTrackedGapsState();
        lg.runs = runs;
        lg.tracked = new bool[lg.runs.Count];
        lg.i = 0;
        while (lg.i < lg.runs.Count)
        {
            if (!TrackLetterGapRun(lg)) break;
        }
        return lg.tracked;
    }

    /// <summary>
    /// Computes the trailing Tc/spacing contribution at the end of the last matched run.
    /// This value is subtracted from bg rect width so it covers only visible text.
    /// </summary>
    private static double ComputeTrailingTc(List<RawTextRun> rawFragments, int[] runStartChar,
        int lastRunIdx, int endCharIdx)
    {
        var lastRun = rawFragments[lastRunIdx];
        var matchEndInRun = endCharIdx - runStartChar[lastRunIdx] + 1;
        if (matchEndInRun >= 2
            && lastRun.CharCumWidths is not null && matchEndInRun < lastRun.CharCumWidths.Length
            && lastRun.Metrics is not null)
        {
            var lastCharAdvance = lastRun.CharCumWidths[matchEndInRun] - lastRun.CharCumWidths[matchEndInRun - 1];
            var lastCharText = lastRun.Text[(matchEndInRun - 1)..matchEndInRun];
            var lastGlyphW = lastRun.Metrics.MeasureString(lastCharText, lastRun.FontSize);
            var tcUnscaled = lastCharAdvance - lastGlyphW;
            // Only the Tc/Tw SPACING part of the excess advance is trimmed off the
            // highlight. An excess from a TJ kern is layout (a tab-like gap the
            // producer drew into the line), and the highlight keeps covering it —
            // the fragment rectangle spans to where the next run starts.
            var trailingSpacing = lastRun.CharSpacing
                + (lastCharText == " " ? lastRun.WordSpacing : 0);
            tcUnscaled = Math.Min(tcUnscaled, trailingSpacing);
            if (tcUnscaled > 0.01)
                return tcUnscaled * lastRun.HScaling * Math.Abs(lastRun.TmA);
        }
        return 0;
    }

    /// <summary>Fills <see cref="TextSegment.Characters"/> with one entry per
    /// character in the segment, each carrying the character's page-space position
    /// and glyph bounding rectangle. Reuses the segment position/rectangle math
    /// applied to a single-character range.</summary>
    /// <summary>
    /// Some embedded/subset fonts can't measure individual glyphs — per-character
    /// advance comes back as 0 even though the run's total width is correct — which
    /// collapses the cumulative-width array to <c>[0,…,0,total]</c>. That would place
    /// every character but the last at the run origin (breaking per-char
    /// <see cref="CharInfo.Rectangle"/> and, in turn, marked-text extraction). When
    /// that degenerate shape is detected, distribute the total width evenly across
    /// the characters. No-op for well-formed arrays.
    /// </summary>
    private static void NormalizeDegenerateCumWidths(double[]? cum)
    {
        if (cum is not { Length: > 2 }) return;
        var total = cum[cum.Length - 1];
        if (total <= 0) return;
        var degenerate = false;
        for (var i = 1; i < cum.Length - 1; i++)
            if (cum[i] <= 0) { degenerate = true; break; }
        if (!degenerate) return;
        var n = cum.Length - 1;
        for (var i = 0; i <= n; i++) cum[i] = total * i / n;
    }
}
