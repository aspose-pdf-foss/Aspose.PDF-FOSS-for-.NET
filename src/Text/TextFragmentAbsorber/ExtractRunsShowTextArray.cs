using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Shows a TJ array: decodes each string piece with its per-character advances, applies the kerning adjustments (synthesising a space at a wide back-jump or a tracking outlier), and emits one raw text run at the seat before advancing the pen.</summary>
    private static void ShowTextArray(ExtractRunsState xr, PdfArray arr)
    {
        var tj = new ShowTextArrayState();
        tj.xr = xr;
        tj.arr = arr;
        tj.sb = new StringBuilder();
        tj.tjWidth = 0;
        tj.tjWidthUnscaled = 0; // same as tjWidth but without hScaling
        tj.segTx = tj.xr.tx;
        tj.segTy = tj.xr.ty;
        tj.consumedW = 0;
        tj.lastStrLen = 0; // decoded length of last PdfString element
        tj.charCumWidthsList = new List<double>();
        tj.charEndPositionsList = new List<double>();

        tj.tjIsType0 = tj.xr.fontDict?.GetName("Subtype") == "Type0";
        tj.tjPieceCount = 0;
        tj.tjMultiGlyphPiece = false;
        tj.tjAdjList = new List<double>();
        foreach (var pre in tj.arr)
            if (pre is PdfString preS0)
            {
                tj.tjPieceCount++;
                if (preS0.Value.Length >= (tj.tjIsType0 ? 4 : 2)) tj.tjMultiGlyphPiece = true;
            }
            else
                tj.tjAdjList.Add(GetNum(pre));
        tj.tjArmed = tj.tjMultiGlyphPiece;
        if (!tj.tjArmed)
            foreach (var pre in tj.arr)
            {
                if (pre is not PdfString preS) continue;
                var preDec = DecodeBytes(preS.Value, tj.xr.toUnicode, tj.xr.fontDict, tj.xr.reader, tj.xr.useFontEngineEncoding);
                if (preDec.Length >= 2) { tj.tjArmed = true; tj.tjMultiGlyphPiece = true; break; }
                var preArm = false;
                foreach (var preC in preDec)
                    if (!char.IsUpper(preC) && !char.IsPunctuation(preC))
                    { preArm = true; break; }
                if (preArm) { tj.tjArmed = true; break; }
            }
        tj.tjSynthSpaces = tj.tjArmed && tj.tjPieceCount >= 2
            && (tj.tjPieceCount <= 10 || tj.tjMultiGlyphPiece);
        tj.tjLtrackMedian = double.NaN;
        if (!tj.tjSynthSpaces && !tj.tjMultiGlyphPiece && tj.tjPieceCount >= 3 && tj.tjAdjList.Count >= 2)
        {
            tj.tjAdjList.Sort();
            tj.tjLtrackMedian = tj.tjAdjList[tj.tjAdjList.Count / 2];
        }

        for (int tjIdx = 0; tjIdx < tj.arr.Count; tjIdx++)
        {
            ShowTextArrayItem(tj, tjIdx);
        }
        // Add n+1 entry (total width) for trailing Tc detection and clipping.
        if (tj.charCumWidthsList.Count == tj.sb.Length)
            tj.charCumWidthsList.Add(tj.tjWidthUnscaled);
        tj.charCumWidths = tj.charCumWidthsList.Count == tj.sb.Length + 1
            ? tj.charCumWidthsList.ToArray() : null;
        NormalizeDegenerateCumWidths(tj.charCumWidths);
        tj.charEndPositions = tj.charEndPositionsList.Count == tj.sb.Length
            ? tj.charEndPositionsList.ToArray() : null;
        // Use unscaled width for rectangle computation (CTM handles visual scaling)
        tj.xr.result.Add(new RawTextRun(tj.sb.ToString(), tj.segTx, tj.segTy, tj.xr.fontSize, tj.xr.currentFontName, tj.tjWidthUnscaled, tj.xr.ctm, tj.xr.metrics,
            TmA: tj.xr.tmA, TmB: tj.xr.tmB, TmC: tj.xr.tmC, TmD: tj.xr.tmD, CharCumWidths: tj.charCumWidths,
            CharEndPositions: tj.charEndPositions, RenderingMode: tj.xr.renderMode, LineWidth: tj.xr.currentLineWidth,
            IsBold: tj.xr.currentIsBold, IsItalic: tj.xr.currentIsItalic, FontInfoObj: tj.xr.currentFontInfo,
            HScaling: tj.xr.hScaling, TextRise: tj.xr.textRise, FillColor: tj.xr.currentFillColor, StrokingColor: tj.xr.currentStrokeColor,
            ClipRect: tj.xr.currentClip, CharSpacing: tj.xr.charSpacing, WordSpacing: tj.xr.wordSpacing, TmBaseY: tj.xr.tmBaseTy,
            InkCharStart: tj.inkStart, InkCharEnd: tj.inkEnd));
        tj.xr.lastEmittedY = tj.xr.ty;
        (_, tj.xr.lastEmittedPageY) = ApplyCtm(tj.xr.tx, tj.xr.ty, tj.xr.ctm);
        tj.xr.lastEmittedFs = tj.xr.fontSize;
        // Advance position through text matrix (for rotated text tmB≠0 advances Y)
        tj.xr.tx += tj.xr.tmA * (tj.consumedW + tj.tjWidth);
        tj.xr.ty += tj.xr.tmB * (tj.consumedW + tj.tjWidth);
    }

    /// <summary>One TJ array element: a string piece decoded and measured character by character onto the cumulative widths, or a kerning adjustment that may synthesise a word space.</summary>
    private static void ShowTextArrayItem(ShowTextArrayState tj, int tjIdx)
    {
        var item = tj.arr[tjIdx];
        if (item is PdfString ps)
        {
            var decoded = DecodeBytes(ps.Value, tj.xr.toUnicode, tj.xr.fontDict, tj.xr.reader, tj.xr.useFontEngineEncoding);
            tj.lastStrLen = decoded.Length;
            // Build per-character cumulative widths from byte-level metrics
            // so that TJ kerning before/between segments is correctly tracked.
            double segAdvance = 0;
            if (tj.xr.metrics is not null)
            {
                // Detect CID font: 2 bytes per character.
                int byteLen = (ps.Value.Length > 0 && decoded.Length > 0
                    && ps.Value.Length == decoded.Length * 2) ? 2 : 1;
                for (var ci = 0; ci < ps.Value.Length; )
                {
                    tj.charCumWidthsList.Add(tj.tjWidthUnscaled + segAdvance);
                    var bl = Math.Min(byteLen, ps.Value.Length - ci);
                    // float-rounded (glyph advances live in
                    // float32; logged widths carry the float noise —
                    // "26.79240010261536" — and tests compare log LENGTHS).
                    var charW = (double)(float)tj.xr.metrics.MeasureStringExact(ps.Value[ci..(ci + bl)], tj.xr.fontSize);
                    var charIdx = byteLen == 2 ? ci / 2 : ci;
                    var isSpace = charIdx < decoded.Length && decoded[charIdx] == ' ';
                    var advance = charW + tj.xr.charSpacing + (isSpace ? tj.xr.wordSpacing : 0);
                    segAdvance += advance;
                    tj.charEndPositionsList.Add(tj.tjWidthUnscaled + segAdvance);
                    ci += bl;
                }
            }
            else
            {
                // No metrics: distribute total width proportionally
                for (var ci = 0; ci < decoded.Length; ci++)
                {
                    tj.charCumWidthsList.Add(tj.tjWidthUnscaled + segAdvance);
                    tj.charEndPositionsList.Add(tj.tjWidthUnscaled + segAdvance);
                }
            }
            if (decoded.Trim().Length > 0)
            {
                if (tj.inkStart < 0) tj.inkStart = tj.sb.Length;
                tj.inkEnd = tj.sb.Length + decoded.Length;
            }
            tj.sb.Append(decoded);
            var segW = (double)(float)(tj.xr.metrics?.MeasureStringExact(ps.Value, tj.xr.fontSize) ?? 0);
            var segSpaces = decoded.Count(c => c == ' ');
            var unscaledAdvance = segW + tj.xr.charSpacing * decoded.Length + tj.xr.wordSpacing * segSpaces;
            tj.tjWidth += unscaledAdvance * tj.xr.hScaling;
            tj.tjWidthUnscaled += unscaledAdvance;
        }
        else
        {
            // Kerning adjustment: value in thousandths of text space unit
            // Negative values move right, positive move left
            var adj = GetNum(item);
            var kernPt = -adj * tj.xr.fontSize / 1000.0;
            tj.tjWidth += kernPt * tj.xr.hScaling;
            tj.tjWidthUnscaled += kernPt;

            // Insert ONE synthetic space per adjustment ≤ −130 when the
            // run is eligible (see the prescan note above). The only
            // suppression is a space GLYPH immediately left of the gap
            // (a kern between a real space and the next word never
            // doubles); a real space FOLLOWING the gap does not
            // suppress — "T·−175·(sp)" extracts as "T␣␣".
            // A LARGE POSITIVE adjustment (≥1 em) is a backward pen jump —
            // a producer drawing same-row columns right-to-left inside one
            // TJ ('14.400'(+8691)'14.650') — UNLESS the pen lands just
            // right of an already-drawn CHAR's start (within ~1 em): a
            // draw-order zigzag continuing a visually contiguous token
            // ('1'(+13341)'1' landing one glyph right of the prior '1' in
            // a giant-advance font) stays glued. Char STARTS, not advance
            // ends — these producers carry column pitch in the advances.
            var backJumpBreaks = adj >= 1000;
            if (backJumpBreaks)
                foreach (var cs in tj.charCumWidthsList)
                {
                    var d = tj.tjWidthUnscaled - cs;
                    if (d > 0 && d <= 1.0 * tj.xr.fontSize) { backJumpBreaks = false; break; }
                }
            if (((tj.tjSynthSpaces && adj <= -130)
                 || (!double.IsNaN(tj.tjLtrackMedian) && adj - tj.tjLtrackMedian <= -130
                     && (tj.tjLtrackMedian >= 0 || adj <= -250))
                 || backJumpBreaks)
                && tj.sb.Length > 0 && tj.sb[^1] != ' ')
            {
                tj.sb.Append(' ');
                tj.charCumWidthsList.Add(tj.tjWidthUnscaled); // space inserted at current position
                tj.charEndPositionsList.Add(tj.tjWidthUnscaled);
            }
        }
    }
}
