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
        var tracked = new bool[runs.Count];
        var i = 0;
        while (i < runs.Count)
        {
            if (runs[i].Text == "\r\n") { i++; continue; }

            // Gather one line: consecutive content runs sharing a baseline. Compare each run
            // to the PREVIOUS run's Y (not the line's first) so a slightly sloped/italic
            // glyph-by-glyph baseline (a small consistent per-glyph drift) stays one line
            // instead of fragmenting — matching the guard site's adjacent deltaY check.
            var line = new List<int>();
            var prevLineY = runs[i].Y;
            var j = i;
            while (j < runs.Count)
            {
                if (runs[j].Text == "\r\n") { j++; continue; }
                if (line.Count > 0 && Math.Abs(runs[j].Y - prevLineY) >= 2.0) break;
                line.Add(j);
                prevLineY = runs[j].Y;
                j++;
            }
            var lineY = runs[i].Y;

            // Need ≥3 gaps (≥4 runs) to call a pattern "uniform tracking".
            if (line.Count >= 4)
            {
                var gaps = new double[line.Count];
                var subWord = new double[line.Count]; // 0.6·effFont ceiling per gap
                // A gap bordered by a MULTI-WORD run (a real space glyph among other
                // characters) is a WORD boundary, never intra-word letter tracking:
                // tracking splits one word into letter runs ("M","ARK"), while a
                // justified line drawn word-per-Tm has uniform ~space-sized gaps between
                // whole phrases ("…to 24"|"MAR"|"2013. During…") that must keep their
                // word spaces. A run that IS whitespace (an explicit space-glyph run
                // between tracked letters — 'M'|'ARK'|' '|'A.') stays trackable: such
                // lines mark their word breaks with the space runs themselves.
                // ★ Tracking is a LATIN phenomenon: it spreads the letters of one word,
                // which is why suppressing its gaps keeps the word whole. A CJK glyph is
                // not a letter of a word — a name set with its ideographs spread evenly
                // across a fixed column width looks identical to tracked text, and the
                // gaps there are real: a page that draws '監 察 監 督 官' glyph by glyph
                // reads back with a space at every gap. So a gap facing an ideograph or
                // kana is never claimed as tracking.
                var cjky = new bool[line.Count];
                var wordy = new bool[line.Count];
                for (var k = 0; k < line.Count; k++)
                {
                    var t = runs[line[k]].Text;
                    wordy[k] = t.Contains(' ') && t.Trim().Length > 0;
                    cjky[k] = false;
                    foreach (var ch in t)
                        if (IsCjk(ch)) { cjky[k] = true; break; }
                }
                for (var k = 1; k < line.Count; k++)
                {
                    var prev = runs[line[k - 1]];
                    var cur = runs[line[k]];
                    var prevEndX = prev.X + (prev.Width > 0 ? prev.Width * prev.HScaling : EstimateWidth(prev.Text, prev.FontSize));
                    gaps[k] = cur.X - prevEndX;
                    var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
                    var sx = Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 1.0;
                    subWord[k] = 0.6 * fs * sx;
                }

                // Letter-tracking splits a WORD into short pieces ("M","ARK"): a
                // window only counts when its runs are word FRAGMENTS (one side a
                // 1–2 char piece, neither side a whole 4+ char word). Justified
                // prose drawn word-per-run also has uniform sub-word gaps, but its
                // runs are whole words — suppressing those spaces glued sentences
                // ("…accessanduseServices…").
                bool PieceLike(int ka, int kb)
                {
                    var la = runs[line[ka]].Text.Trim().Length;
                    var lb = runs[line[kb]].Text.Trim().Length;
                    return (la <= 2 || lb <= 2) && la < 4 && lb < 4;
                }
                var k0 = 1;
                while (k0 < line.Count)
                {
                    // Seed a window on a positive, sub-word-sized gap between space-free runs.
                    if (!(gaps[k0] > 0 && gaps[k0] < subWord[k0] && !wordy[k0 - 1] && !wordy[k0]
                          && !cjky[k0 - 1] && !cjky[k0]
                          && PieceLike(k0 - 1, k0))) { k0++; continue; }
                    var k1 = k0;
                    while (k1 + 1 < line.Count
                        && gaps[k1 + 1] > 0
                        && gaps[k1 + 1] < subWord[k1 + 1]
                        && !wordy[k1] && !wordy[k1 + 1]
                        && !cjky[k1] && !cjky[k1 + 1]
                        && PieceLike(k1, k1 + 1)
                        && Math.Abs(gaps[k1 + 1] - gaps[k0]) <= Math.Max(0.5, 0.2 * gaps[k0]))
                    {
                        k1++;
                    }
                    if (k1 - k0 + 1 >= 3)
                        for (var k = k0; k <= k1; k++) tracked[line[k]] = true;
                    k0 = Math.Max(k1 + 1, k0 + 1);
                }

                // Continuous letter-tracking (NON-uniform): within a maximal run of consecutive
                // single-char glyphs, if a MAJORITY of inter-glyph gaps carry a word-sized gap,
                // the run is one token spelled out with loose per-glyph spacing (every letter is
                // gapped) — none of the gaps are real word breaks (cf. a loosely-tracked
                // "American" or a code "ADED1"). This is distinguished from genuinely
                // word-separated glyph-by-glyph text (an OCR overlay) where letters are packed
                // tight and only a MINORITY of gaps — the actual word spaces — exceed the
                // threshold. Applied per single-char RUN (not per line) so a glyph-by-glyph
                // token embedded among coalesced words is still handled.
                {
                    var s = 0;
                    while (s < line.Count)
                    {
                        if (runs[line[s]].Text.Length != 1) { s++; continue; }
                        var e = s;
                        while (e + 1 < line.Count && runs[line[e + 1]].Text.Length == 1) e++;
                        // [s..e] is a maximal single-char run; its gaps are at k=s+1..e.
                        var totalSs = e - s;
                        if (totalSs >= 3)
                        {
                            var overThr = 0;
                            var packed = 0;
                            var doubled = 0;
                            for (var k = s + 1; k <= e; k++)
                            {
                                var fs = runs[line[k]].FontSize > 0 ? runs[line[k]].FontSize : 12.0;
                                var sx = Math.Abs(runs[line[k]].TmA) > 0 ? Math.Abs(runs[line[k]].TmA) : 1.0;
                                if (gaps[k] > 0.2 * fs * sx) overThr++;
                                // With Tz-scaled widths a genuinely packed glyph pair CLOSES: the
                                // next glyph starts left of the previous glyph's rendered right edge
                                // (a negative gap). Word-spaced glyph text packs DIFFERENT glyphs
                                // tight like this and opens only at the sparse real word breaks.
                                if (gaps[k] < 0) packed++;
                                // Drop-shadow doubling (e.g. an IgnoreShadowText source: "CCoonn…")
                                // repeats the SAME character across each small negative overlap; its
                                // inter-letter advances read word-sized so overThr is high, but it is
                                // NOT word-spaced and must stay tracked so the de-shadowed word is not
                                // split. Char-doubling separates it from real word-spaced glyph text.
                                if (runs[line[k]].Text == runs[line[k - 1]].Text) doubled++;
                            }
                            // Loose/uniform letter-tracking ("American", "ADED1") keeps every gap a
                            // similar POSITIVE amount → overThr high, packed ~0 → tracked. Shadow
                            // doubling → overThr high, packed high, but doubled high → tracked. Only
                            // tight word-spaced glyph text (packed high, doubled low) is left alone so
                            // its real word breaks survive.
                            bool shadowLike = doubled >= totalSs * 0.3;
                            bool wordSpaced = !shadowLike && packed >= totalSs * 0.4;
                            if (overThr >= totalSs * 0.4 && !wordSpaced)
                                for (var k = s + 1; k <= e; k++)
                                    if (!cjky[k - 1] && !cjky[k]) tracked[line[k]] = true;
                        }
                        s = e + 1;
                    }
                }
            }

            i = j;
        }
        return tracked;
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
