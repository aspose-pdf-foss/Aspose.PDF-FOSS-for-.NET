using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Two runs the baselines put on the same line: the gap between them earns a separator space unless it is letter-tracking, a squeezed set, a drop-shadow repeat or a boundary the line never drew.</summary>
    private bool InsertSameLineGapSpace(List<RawTextRun> rawFragments, int i, int prevIdx, RawTextRun prev, double deltaY, StringBuilder fullText, List<int> charToRun, int[] runStartChar, bool ignoreShadow, ref string? lastKeptText, ref double lastKeptX, ref double lastKeptY, bool[] letterTracked, bool[] squeezedGap)
    {
        // Remove \r\n sentinels between prevIdx and i on the same line —
        // they were BT/ET boundary artifacts, not real line breaks.
        if (prevIdx < i - 1) DropLineSentinels(fullText, charToRun);

        if (TryPrependBackJumpRun(rawFragments, i, prev, fullText, charToRun, runStartChar))
        {
            if (ignoreShadow)
            {
                lastKeptText = rawFragments[i].Text;
                lastKeptX = rawFragments[i].X;
                lastKeptY = rawFragments[i].Y;
            }
            return true;
        }

        if (NeedsSameLineGapSpace(rawFragments, i, prev, deltaY, fullText, letterTracked, squeezedGap))
        {
            charToRun.Add(prevIdx);
            fullText.Append(' ');
        }
        return false;
    }

    /// <summary>Drops the line-break sentinels a BT/ET boundary left behind mid-line.</summary>
    private static void DropLineSentinels(StringBuilder fullText, List<int> charToRun)
    {
        while (fullText.Length > 0 && (fullText[^1] == '\r' || fullText[^1] == '\n'))
        {
            fullText.Length--;
            charToRun.RemoveAt(charToRun.Count - 1);
        }
    }

    /// <summary>Back-jump PREPEND: a same-row run drawn wholly LEFT of the line's current start
    /// splices in FRONT of the line, X-ordered (a stream that draws a value before its label
    /// reads "Kundenummer: 981641205"). Junction separator: exactly one space iff the x-gap
    /// between the run's end and the line start is ≥ 0.15 em. RTL runs keep the append path —
    /// their visual pen legitimately walks right-to-left and reorders via the bidi pass.</summary>
    private bool TryPrependBackJumpRun(List<RawTextRun> rawFragments, int i, RawTextRun prev,
        StringBuilder fullText, List<int> charToRun, int[] runStartChar)
    {
        var cur = rawFragments[i];
        var bjText = cur.Text;
        var bjHasRtl = false;
        foreach (var bjc in bjText)
            if (BidiReorderer.IsRtlChar(bjc)) { bjHasRtl = true; break; }
        // UPRIGHT text only: rotated/vertical runs advance along Y, so
        // an X-based "wholly left" test would reorder reading order.
        var bjUpright = Math.Abs(cur.TmB) <= Math.Abs(cur.TmA) * 1e-3
            && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA) * 1e-3
            && IsUprightCtm(cur) && IsUprightCtm(prev);
        if (!bjUpright || bjHasRtl || bjText.Trim().Length == 0) return false;

        var lsIdx = fullText.Length;
        while (lsIdx > 0 && fullText[lsIdx - 1] != '\n') lsIdx--;
        var lineMinX = double.NaN;
        for (var cc = lsIdx; cc < charToRun.Count; cc++)
        {
            var rIdx = charToRun[cc];
            if (rIdx < 0 || rIdx >= rawFragments.Count) continue;
            var rx = rawFragments[rIdx].X;
            if (double.IsNaN(lineMinX) || rx < lineMinX) lineMinX = rx;
        }
        var bjW = cur.Width > 0 ? cur.Width * cur.HScaling : EstimateWidth(bjText, cur.FontSize);
        var bjEnd = cur.X + bjW;
        var bjFs = cur.FontSize > 0 ? cur.FontSize : 12.0;
        if (double.IsNaN(lineMinX) || bjEnd > lineMinX + 0.5) return false;

        var junction = lineMinX - bjEnd;
        var sep = junction >= bjFs * 0.15 ? " " : "";
        var insertText = bjText + sep;
        fullText.Insert(lsIdx, insertText);
        var entries = new int[insertText.Length];
        for (var k = 0; k < entries.Length; k++) entries[k] = i;
        charToRun.InsertRange(lsIdx, entries);
        for (var rj = 0; rj < i; rj++)
            if (runStartChar[rj] >= lsIdx) runStartChar[rj] += insertText.Length;
        runStartChar[i] = lsIdx;
        return true;
    }

    /// <summary>Insert a space if there's a word-sized or column-sized gap.</summary>
    private bool NeedsSameLineGapSpace(List<RawTextRun> rawFragments, int i, RawTextRun prev,
        double deltaY, StringBuilder fullText, bool[] letterTracked, bool[] squeezedGap)
    {
        var cur = rawFragments[i];
        // Widths are TEXT-space (they scale with Tm) while X is the Tm
        // translation — scale the width by the run's Tm X-scale so the
        // gap is measured in one space (a `/F 1 Tf` + `7 0 0 7 … Tm`
        // producer otherwise reads every glyph pair as a word gap).
        var prevTmScale = Math.Abs(prev.TmA) > 0 ? Math.Abs(prev.TmA) : 1.0;
        var prevEndX = prev.X + (prev.Width > 0
            ? prev.Width * prev.HScaling
            : EstimateWidth(prev.Text, prev.FontSize)) * prevTmScale;
        var gap = cur.X - prevEndX;
        var fontSize = cur.FontSize > 0 ? cur.FontSize : 12.0;
        var tmScaleX = Math.Abs(cur.TmA) > 0 ? Math.Abs(cur.TmA) : 1.0;
        var effFontSize = fontSize * tmScaleX;
        var lastChar = fullText.Length > 0 ? fullText[^1] : '\0';
        var nextChar = cur.Text.Length > 0 ? cur.Text[0] : '\0';
        if (fullText.Length == 0 || lastChar == ' ' || lastChar == '\n' || nextChar == ' ')
            return false;

        // A GapSplit continuation is a column sibling the absorber cut out
        // of one show op — always exactly one boundary space, independent
        // of the run-gap heuristics (the ceiling would glue wide columns).
        if (cur.GapSplit) return true;
        if (IsWordGap(cur, prev, gap, effFontSize, deltaY, letterTracked[i])) return true;
        // A run that starts PART-WAY THROUGH the previous run's advance —
        // past its origin, but short of where it ends — was set squeezed,
        // and the pieces read as separate tokens. Japanese full-width
        // punctuation is the everyday case: '）' and '、' carry a full-em
        // advance but are set half-width, so the next glyph lands half an em
        // early. The pen deviates from a flush advance by the same amount a
        // word gap does, just with the opposite sign, so it earns the same
        // separator. Two neighbours this rule must NOT claim: a doubled draw
        // (drop shadow) re-starts AT the previous origin, and a back-jump
        // starts left of it — both keep the handling they already had.
        // Only an ISOLATED squeeze counts — see ComputeSqueezedGaps.
        if (squeezedGap[i]) return true;
        // Same-line column hops (>16 em) always separate tokens — the
        // 3-em word-gap ceiling above only guards mid-range gaps. A
        // BACKWARDS pen jump (>1.5 em) is a new column/segment too (the
        // stream returned to an earlier X on the same row).
        // A backwards pen jump separates columns only when the new run
        // lands entirely LEFT of the previous run's start — an OVERLAPPING
        // redraw (drop shadows, doubled draw) keeps gluing.
        if (gap > effFontSize * 16.0 || gap < -effFontSize * 1.5) return true;
        return IsRotatedColumnHop(cur, prev, fontSize);
    }

    /// <summary>A positive, word-sized gap between two runs that isn't letter-tracking.
    /// The <c>letterTracked</c> guard (see ComputeLetterTrackedGaps) covers glyph-by-glyph
    /// lines whose letters are loosely tracked, so single-char runs need no blanket length
    /// guard — genuine word breaks in tight glyph-by-glyph text (an OCR overlay) are kept.
    /// Requiring both runs >= 2 chars instead dropped the space at a word -> single-char-token
    /// boundary ("level" -> "1"), so a phrase search for "Heading level 1" missed the extracted
    /// "Heading level1".</summary>
    private static bool IsWordGap(RawTextRun cur, RawTextRun prev, double gap,
        double effFontSize, double deltaY, bool letterTracked)
    {
        // Beyond the classic 3-em window a gap STILL separates tokens —
        // 4.8–10.4 em same-row column gaps get spaces
        // ('MCF'→'Energy', 'Dry'→'72-40-097') and token streams
        // get a space at 0.23 em AND 20 em alike — but only on a
        // near-flat baseline (table columns drift ≤ ~0.7 pt; a diagonal
        // watermark's run pair sits many points apart in Y) and only
        // between runs whose facing ends are ALPHANUMERIC: a symbol
        // watermark's decorative halves 7 em apart ('…_+|' / '|+_…')
        // are decoration, not words — no space is emitted
        // there. A SINGLE-glyph pair counts as well: a two-character name
        // spread evenly across a fixed width sets its glyphs 5 em apart and
        // reads as two tokens, while a curved word's glyphs are already held
        // together by the flat-baseline test.
        var alnumAdjacent =
            (prev.Text.Length > 0 && char.IsLetterOrDigit(prev.Text[^1]))
            || (cur.Text.Length > 0 && char.IsLetterOrDigit(cur.Text[0]));
        if (gap <= effFontSize * 0.2 || letterTracked) return false;
        if (gap > effFontSize * 3.0 && !(deltaY < 0.75 && alnumAdjacent)) return false;
        // For a SINGLE-char pair we still require a nearly-flat baseline (small deltaY):
        // on a curved word (a display font following a path) an isolated large X-gap
        // between two glyphs is a curve artifact, not a word space. Multi-char runs are
        // unaffected.
        var bothSingle = prev.Text.Length == 1 && cur.Text.Length == 1;
        if (bothSingle && deltaY >= 0.75) return false;
        // An INVISIBLE (Tr 3) OCR overlay keeps a sentence period glued to
        // the next sentence's capital even at a word-sized gap (one
        // layer: per-glyph Tz, '.'->'H' at 0.39 em reads glued). VISIBLE
        // glyph-by-glyph text takes the plain magnitude law instead — a
        // period is an ordinary glyph there (elsewhere '.'->'W' at 0.28 em
        // spaces, same as its every word boundary).
        return !(bothSingle && prev.Text == "."
                 && prev.RenderingMode == InvisibleTextRenderMode
                 && cur.RenderingMode == InvisibleTextRenderMode);
    }

    /// <summary>Rotated text (|TmB| > |TmA|, e.g. vertical labels rotated ~90°) advances along Y,
    /// not X, so the X-based run gap is meaningless (often negative). For such runs the cross-axis
    /// is X: two runs sharing a baseline but at clearly different X are distinct columns/labels
    /// (CAD grid markers "A","B","C" rotated and spread across the sheet), not one word — a
    /// separator lets a regex word boundary \b form between them. Horizontal text (|TmA| >= |TmB|,
    /// incl. curved/kerned words) is unaffected.</summary>
    private static bool IsRotatedColumnHop(RawTextRun cur, RawTextRun prev, double fontSize)
    {
        if (Math.Abs(cur.TmB) <= Math.Abs(cur.TmA) || Math.Abs(prev.TmB) <= Math.Abs(prev.TmA))
            return false;
        var rotScale = Math.Sqrt(cur.TmA * cur.TmA + cur.TmB * cur.TmB);
        var effRotFont = (fontSize > 0 ? fontSize : 12.0) * (rotScale > 0 ? rotScale : 1.0);
        return Math.Abs(cur.X - prev.X) > effRotFont * 0.5;
    }
}
