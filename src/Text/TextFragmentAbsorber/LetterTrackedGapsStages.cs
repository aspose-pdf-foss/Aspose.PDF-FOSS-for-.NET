using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Examines the run at the cursor: a letter-tracked run (uniform pen advances between single glyphs) marks the gaps it spans as tracked, else the cursor moves on.</summary>
    private static bool TrackLetterGapRun(LetterTrackedGapsState lg)
    {
        if (lg.runs[lg.i].Text == "\r\n") { lg.i++; return true; }

        lg.line = new List<int>();
        lg.prevLineY = lg.runs[lg.i].Y;
        lg.j = lg.i;
        while (lg.j < lg.runs.Count)
        {
            if (lg.runs[lg.j].Text == "\r\n") { lg.j++; continue; }
            if (lg.line.Count > 0 && Math.Abs(lg.runs[lg.j].Y - lg.prevLineY) >= 2.0) break;
            lg.line.Add(lg.j);
            lg.prevLineY = lg.runs[lg.j].Y;
            lg.j++;
        }
        lg.lineY = lg.runs[lg.i].Y;

        // Need ≥3 gaps (≥4 runs) to call a pattern "uniform tracking".
        if (lg.line.Count >= 4)
        {
            if (TrackLetterGapLine(lg)) return true;
        }

        lg.i = lg.j;
        return true;
    }

    /// <summary>A line of four or more runs: measures the pen advances between its single-glyph runs and, when they are uniform (letter tracking), marks the gaps across the line as tracked and moves the cursor past the line. True when the line was consumed that way.</summary>
    private static bool TrackLetterGapLine(LetterTrackedGapsState lg)
    {
        lg.gaps = new double[lg.line.Count];
        lg.subWord = new double[lg.line.Count]; // 0.6·effFont ceiling per gap
        lg.cjky = new bool[lg.line.Count];
        lg.wordy = new bool[lg.line.Count];
        for (var k = 0; k < lg.line.Count; k++)
        {
            var t = lg.runs[lg.line[k]].Text;
            lg.wordy[k] = t.Contains(' ') && t.Trim().Length > 0;
            lg.cjky[k] = false;
            foreach (var ch in t)
                if (IsCjk(ch)) { lg.cjky[k] = true; break; }
        }
        for (var k = 1; k < lg.line.Count; k++)
        {
            var prev = lg.runs[lg.line[k - 1]];
            var cur = lg.runs[lg.line[k]];
            var prevEndX = prev.X + (prev.Width > 0 ? prev.Width * prev.HScaling : EstimateWidth(prev.Text, prev.FontSize));
            lg.gaps[k] = cur.X - prevEndX;
            var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
            var sx = Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 1.0;
            lg.subWord[k] = 0.6 * fs * sx;
        }

        // Letter-tracking splits a WORD into short pieces ("M","ARK"): a
        // window only counts when its runs are word FRAGMENTS (one side a
        // 1–2 char piece, neither side a whole 4+ char word). Justified
        // prose drawn word-per-run also has uniform sub-word gaps, but its
        // runs are whole words — suppressing those spaces glued sentences
        // ("…accessanduseServices…").
        bool PieceLike(int ka, int kb)
        {
            var la = lg.runs[lg.line[ka]].Text.Trim().Length;
            var lb = lg.runs[lg.line[kb]].Text.Trim().Length;
            return (la <= 2 || lb <= 2) && la < 4 && lb < 4;
        }
        lg.k0 = 1;
        while (lg.k0 < lg.line.Count)
        {
            // Seed a window on a positive, sub-word-sized gap between space-free runs.
            if (!(lg.gaps[lg.k0] > 0 && lg.gaps[lg.k0] < lg.subWord[lg.k0] && !lg.wordy[lg.k0 - 1] && !lg.wordy[lg.k0]
                  && !lg.cjky[lg.k0 - 1] && !lg.cjky[lg.k0]
                  && PieceLike(lg.k0 - 1, lg.k0))) { lg.k0++; continue; }
            var k1 = lg.k0;
            while (k1 + 1 < lg.line.Count
                && lg.gaps[k1 + 1] > 0
                && lg.gaps[k1 + 1] < lg.subWord[k1 + 1]
                && !lg.wordy[k1] && !lg.wordy[k1 + 1]
                && !lg.cjky[k1] && !lg.cjky[k1 + 1]
                && PieceLike(k1, k1 + 1)
                && Math.Abs(lg.gaps[k1 + 1] - lg.gaps[lg.k0]) <= Math.Max(0.5, 0.2 * lg.gaps[lg.k0]))
            {
                k1++;
            }
            if (k1 - lg.k0 + 1 >= 3)
                for (var k = lg.k0; k <= k1; k++) lg.tracked[lg.line[k]] = true;
            lg.k0 = Math.Max(k1 + 1, lg.k0 + 1);
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
            MarkTrackedSingleGlyphSpans(lg);
        }
        return false;
    }

    /// <summary>Walks the line's spans of single-glyph runs: a span of four or more whose gaps are mostly over the tracking threshold (and not word-spaced) marks its non-CJK gaps as tracked.</summary>
    private static void MarkTrackedSingleGlyphSpans(LetterTrackedGapsState lg)
    {
        var s = 0;
        while (s < lg.line.Count)
        {
            if (lg.runs[lg.line[s]].Text.Length != 1) { s++; continue; }
            var e = s;
            while (e + 1 < lg.line.Count && lg.runs[lg.line[e + 1]].Text.Length == 1) e++;
            // [s..e] is a maximal single-char run; its gaps are at k=s+1..e.
            var totalSs = e - s;
            if (totalSs >= 3)
            {
                var overThr = 0;
                var packed = 0;
                var doubled = 0;
                for (var k = s + 1; k <= e; k++)
                {
                    var fs = lg.runs[lg.line[k]].FontSize > 0 ? lg.runs[lg.line[k]].FontSize : 12.0;
                    var sx = Math.Abs(lg.runs[lg.line[k]].TmA) > 0 ? Math.Abs(lg.runs[lg.line[k]].TmA) : 1.0;
                    if (lg.gaps[k] > 0.2 * fs * sx) overThr++;
                    // With Tz-scaled widths a genuinely packed glyph pair CLOSES: the
                    // next glyph starts left of the previous glyph's rendered right edge
                    // (a negative gap). Word-spaced glyph text packs DIFFERENT glyphs
                    // tight like this and opens only at the sparse real word breaks.
                    if (lg.gaps[k] < 0) packed++;
                    // Drop-shadow doubling (e.g. an IgnoreShadowText source: "CCoonn…")
                    // repeats the SAME character across each small negative overlap; its
                    // inter-letter advances read word-sized so overThr is high, but it is
                    // NOT word-spaced and must stay tracked so the de-shadowed word is not
                    // split. Char-doubling separates it from real word-spaced glyph text.
                    if (lg.runs[lg.line[k]].Text == lg.runs[lg.line[k - 1]].Text) doubled++;
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
                        if (!lg.cjky[k - 1] && !lg.cjky[k]) lg.tracked[lg.line[k]] = true;
            }
            s = e + 1;
        }
    }
}
