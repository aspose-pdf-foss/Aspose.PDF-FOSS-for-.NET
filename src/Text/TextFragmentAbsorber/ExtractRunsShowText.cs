using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShowTextOp(ExtractRunsState xr)
    {
        EnsureFontSet(xr, "Tj");
        if (xr.currentFontMissing) return;
        if (xr.operands.Count >= 1 && xr.operands[0] is PdfString s)
        {
            var text = DecodeBytes(s.Value, xr.toUnicode, xr.fontDict, xr.reader, xr.useFontEngineEncoding);
            var rawWidth = xr.metrics?.MeasureStringExact(s.Value, xr.fontSize) ?? 0;
            var numChars = text.Length;
            var numSpaces = text.Count(c => c == ' ');
            var unscaledWidth = rawWidth + xr.charSpacing * numChars + xr.wordSpacing * numSpaces;
            var scaledWidth = unscaledWidth * xr.hScaling;
            // Build per-character cumulative widths from byte-level
            // metrics so segment positioning is consistent with how
            // tx is advanced. Without this, MeasureString(string)
            // may give different results than MeasureString(bytes)
            // for fonts with custom encodings or differing glyph
            // indices, causing segment X offsets to drift.
            double[]? tjCharCumWidths = null;
            if (xr.metrics is not null && text.Length == s.Value.Length)
            {
                // n+1 entries: cumWidths[i] = advance to start of char i;
                // cumWidths[n] = total advance past last char (incl. trailing Tc).
                var cumWidths = new double[text.Length + 1];
                double cumW = 0;
                for (var ci = 0; ci < s.Value.Length; ci++)
                {
                    cumWidths[ci] = cumW;
                    var charW = xr.metrics.MeasureStringExact(
                        s.Value[ci..(ci + 1)], xr.fontSize);
                    var isSpace = ci < text.Length && text[ci] == ' ';
                    cumW += charW + xr.charSpacing
                        + (isSpace ? xr.wordSpacing : 0);
                }
                cumWidths[text.Length] = cumW;
                tjCharCumWidths = cumWidths;
            }
            else if (xr.metrics is not null && text.Length > 0
                && s.Value.Length == text.Length * 2)
            {
                // CID font: 2 bytes per character
                var cumWidths = new double[text.Length + 1];
                double cumW = 0;
                for (var ci = 0; ci < text.Length; ci++)
                {
                    cumWidths[ci] = cumW;
                    var charW = xr.metrics.MeasureStringExact(
                        s.Value[(ci * 2)..(ci * 2 + 2)], xr.fontSize);
                    cumW += charW + xr.charSpacing
                        + (text[ci] == ' ' ? xr.wordSpacing : 0);
                }
                cumWidths[text.Length] = cumW;
                tjCharCumWidths = cumWidths;
            }
            else if (xr.metrics is not null && text.Length > 0
                && s.Value.Length != text.Length)
            {
                // Other encoding mismatch: distribute proportionally
                // from byte-level measured width
                var cumWidths = new double[text.Length + 1];
                for (var ci = 0; ci <= text.Length; ci++)
                    cumWidths[ci] = unscaledWidth * ci / text.Length;
                tjCharCumWidths = cumWidths;
            }

            NormalizeDegenerateCumWidths(tjCharCumWidths);
            // RawTextRun.Width stores unscaled width (CTM handles visual scaling)
            xr.result.Add(new RawTextRun(text, xr.tx, xr.ty, xr.fontSize, xr.currentFontName, unscaledWidth, xr.ctm, xr.metrics,
                TmA: xr.tmA, TmB: xr.tmB, TmC: xr.tmC, TmD: xr.tmD,
                CharCumWidths: tjCharCumWidths,
                RenderingMode: xr.renderMode, LineWidth: xr.currentLineWidth,
                IsBold: xr.currentIsBold, IsItalic: xr.currentIsItalic, FontInfoObj: xr.currentFontInfo,
                HScaling: xr.hScaling,
                TextRise: xr.textRise,
                FillColor: xr.currentFillColor, StrokingColor: xr.currentStrokeColor,
                ClipRect: xr.currentClip, CharSpacing: xr.charSpacing, WordSpacing: xr.wordSpacing, TmBaseY: xr.tmBaseTy));
            xr.lastEmittedY = xr.ty;
            (_, xr.lastEmittedPageY) = ApplyCtm(xr.tx, xr.ty, xr.ctm);
            xr.lastEmittedFs = xr.fontSize;
            // Advance position uses scaled width
            xr.tx += xr.tmA * scaledWidth;
            xr.ty += xr.tmB * scaledWidth;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShowTextArrayOp(ExtractRunsState xr)
    {
        EnsureFontSet(xr, "TJ");
        if (xr.currentFontMissing) return;
        if (xr.operands.Count >= 1 && xr.operands[0] is PdfArray arr)
        {
            var sb = new StringBuilder();
            double tjWidth = 0;
            double tjWidthUnscaled = 0; // same as tjWidth but without hScaling
            // Segment origin + consumed advance: a huge intra-TJ kern
            // (> ~1.5 em) SPLITS the array into separate runs at their
            // drawn positions (the Flatten tokenization rule).
            double segTx = xr.tx, segTy = xr.ty, consumedW = 0;
            int lastStrLen = 0; // decoded length of last PdfString element
            // Track per-character cumulative advance widths WITHOUT hScaling.
            // Rectangle width should not include Tz scaling — CTM handles
            // the visual scaling. This matches .NET behavior.
            var charCumWidthsList = new List<double>();
            // Parallel list: position just AFTER each character's own glyph
            // advance, BEFORE any TJ kerning that follows.  Fragment-width
            // computation uses this for the match's final character so that
            // compensation kernings sitting between the matched region and
            // subsequent runs don't inflate the fragment's rectangle.
            var charEndPositionsList = new List<double>();

            // Synthetic-space eligibility (validated over a
            // 1231-run corpus with zero mismatches): a TJ
            // run inserts ONE space per numeric adjustment ≤ −130/1000 em
            // iff it is "armed" — any piece of ≥2 glyphs, or any glyph
            // that is NOT an uppercase letter or punctuation (lowercase,
            // digits, spaces and symbols arm; tracked caps-only display
            // text like "(A)-417(R)-416(K)" collapses in EVERY font type) —
            // AND it is not the letter-tracking shape: an array of MORE
            // than 10 pieces that are ALL single-glyph collapses with no
            // synthetic spaces; word-piece prose arrays of any length keep
            // their kern-encoded word gaps.
            var tjIsType0 = xr.fontDict?.GetName("Subtype") == "Type0";
            var tjPieceCount = 0;
            var tjMultiGlyphPiece = false;
            var tjAdjList = new List<double>();
            foreach (var pre in arr)
                if (pre is PdfString preS0)
                {
                    tjPieceCount++;
                    if (preS0.Value.Length >= (tjIsType0 ? 4 : 2)) tjMultiGlyphPiece = true;
                }
                else
                    tjAdjList.Add(GetNum(pre));
            var tjArmed = tjMultiGlyphPiece;
            if (!tjArmed)
                foreach (var pre in arr)
                {
                    if (pre is not PdfString preS) continue;
                    var preDec = DecodeBytes(preS.Value, xr.toUnicode, xr.fontDict, xr.reader, xr.useFontEngineEncoding);
                    if (preDec.Length >= 2) { tjArmed = true; tjMultiGlyphPiece = true; break; }
                    var preArm = false;
                    foreach (var preC in preDec)
                        if (!char.IsUpper(preC) && !char.IsPunctuation(preC))
                        { preArm = true; break; }
                    if (preArm) { tjArmed = true; break; }
                }
            var tjSynthSpaces = tjArmed && tjPieceCount >= 2
                && (tjPieceCount <= 10 || tjMultiGlyphPiece);
            // Letter-tracked single-glyph arrays (the disarmed shape) can still
            // encode WORD gaps — as kern OUTLIERS against the array's uniform
            // tracking baseline rather than absolute-threshold kerns (letters
            // tracked at +20..+58, words at −135..−169). Break where the
            // adjustment falls ≥130/1000 em BELOW the array's median; a
            // uniformly tracked display run (every kern ≈ the median) still
            // collapses. Mirrors the TextAbsorber rule.
            var tjLtrackMedian = double.NaN;
            if (!tjSynthSpaces && !tjMultiGlyphPiece && tjPieceCount >= 3 && tjAdjList.Count >= 2)
            {
                tjAdjList.Sort();
                tjLtrackMedian = tjAdjList[tjAdjList.Count / 2];
            }

            for (int tjIdx = 0; tjIdx < arr.Count; tjIdx++)
            {
                var item = arr[tjIdx];
                if (item is PdfString ps)
                {
                    var decoded = DecodeBytes(ps.Value, xr.toUnicode, xr.fontDict, xr.reader, xr.useFontEngineEncoding);
                    lastStrLen = decoded.Length;
                    // Build per-character cumulative widths from byte-level metrics
                    // so that TJ kerning before/between segments is correctly tracked.
                    double segAdvance = 0;
                    if (xr.metrics is not null)
                    {
                        // Detect CID font: 2 bytes per character.
                        int byteLen = (ps.Value.Length > 0 && decoded.Length > 0
                            && ps.Value.Length == decoded.Length * 2) ? 2 : 1;
                        for (var ci = 0; ci < ps.Value.Length; )
                        {
                            charCumWidthsList.Add(tjWidthUnscaled + segAdvance);
                            var bl = Math.Min(byteLen, ps.Value.Length - ci);
                            // float-rounded (glyph advances live in
                            // float32; logged widths carry the float noise —
                            // "26.79240010261536" — and tests compare log LENGTHS).
                            var charW = (double)(float)xr.metrics.MeasureStringExact(ps.Value[ci..(ci + bl)], xr.fontSize);
                            var charIdx = byteLen == 2 ? ci / 2 : ci;
                            var isSpace = charIdx < decoded.Length && decoded[charIdx] == ' ';
                            var advance = charW + xr.charSpacing + (isSpace ? xr.wordSpacing : 0);
                            segAdvance += advance;
                            charEndPositionsList.Add(tjWidthUnscaled + segAdvance);
                            ci += bl;
                        }
                    }
                    else
                    {
                        // No metrics: distribute total width proportionally
                        for (var ci = 0; ci < decoded.Length; ci++)
                        {
                            charCumWidthsList.Add(tjWidthUnscaled + segAdvance);
                            charEndPositionsList.Add(tjWidthUnscaled + segAdvance);
                        }
                    }
                    sb.Append(decoded);
                    var segW = (double)(float)(xr.metrics?.MeasureStringExact(ps.Value, xr.fontSize) ?? 0);
                    var segSpaces = decoded.Count(c => c == ' ');
                    var unscaledAdvance = segW + xr.charSpacing * decoded.Length + xr.wordSpacing * segSpaces;
                    tjWidth += unscaledAdvance * xr.hScaling;
                    tjWidthUnscaled += unscaledAdvance;
                }
                else
                {
                    // Kerning adjustment: value in thousandths of text space unit
                    // Negative values move right, positive move left
                    var adj = GetNum(item);
                    var kernPt = -adj * xr.fontSize / 1000.0;
                    tjWidth += kernPt * xr.hScaling;
                    tjWidthUnscaled += kernPt;

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
                        foreach (var cs in charCumWidthsList)
                        {
                            var d = tjWidthUnscaled - cs;
                            if (d > 0 && d <= 1.0 * xr.fontSize) { backJumpBreaks = false; break; }
                        }
                    if (((tjSynthSpaces && adj <= -130)
                         || (!double.IsNaN(tjLtrackMedian) && adj - tjLtrackMedian <= -130
                             && (tjLtrackMedian >= 0 || adj <= -250))
                         || backJumpBreaks)
                        && sb.Length > 0 && sb[^1] != ' ')
                    {
                        sb.Append(' ');
                        charCumWidthsList.Add(tjWidthUnscaled); // space inserted at current position
                        charEndPositionsList.Add(tjWidthUnscaled);
                    }
                }
            }
            // Add n+1 entry (total width) for trailing Tc detection and clipping.
            if (charCumWidthsList.Count == sb.Length)
                charCumWidthsList.Add(tjWidthUnscaled);
            var charCumWidths = charCumWidthsList.Count == sb.Length + 1
                ? charCumWidthsList.ToArray() : null;
            NormalizeDegenerateCumWidths(charCumWidths);
            var charEndPositions = charEndPositionsList.Count == sb.Length
                ? charEndPositionsList.ToArray() : null;
            // Use unscaled width for rectangle computation (CTM handles visual scaling)
            xr.result.Add(new RawTextRun(sb.ToString(), segTx, segTy, xr.fontSize, xr.currentFontName, tjWidthUnscaled, xr.ctm, xr.metrics,
                TmA: xr.tmA, TmB: xr.tmB, TmC: xr.tmC, TmD: xr.tmD, CharCumWidths: charCumWidths,
                CharEndPositions: charEndPositions, RenderingMode: xr.renderMode, LineWidth: xr.currentLineWidth,
                IsBold: xr.currentIsBold, IsItalic: xr.currentIsItalic, FontInfoObj: xr.currentFontInfo,
                HScaling: xr.hScaling, TextRise: xr.textRise, FillColor: xr.currentFillColor, StrokingColor: xr.currentStrokeColor,
                ClipRect: xr.currentClip, CharSpacing: xr.charSpacing, WordSpacing: xr.wordSpacing, TmBaseY: xr.tmBaseTy));
            xr.lastEmittedY = xr.ty;
            (_, xr.lastEmittedPageY) = ApplyCtm(xr.tx, xr.ty, xr.ctm);
            xr.lastEmittedFs = xr.fontSize;
            // Advance position through text matrix (for rotated text tmB≠0 advances Y)
            xr.tx += xr.tmA * (consumedW + tjWidth);
            xr.ty += xr.tmB * (consumedW + tjWidth);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShowTextNextLineOp(ExtractRunsState xr)
    {
        // Move to next line (T* equivalent), then show text
        xr.txLine = xr.tmA * 0 + xr.tmC * (-xr.leading) + xr.txLine;
        xr.tyLine = xr.tmB * 0 + xr.tmD * (-xr.leading) + xr.tyLine;
        xr.tx = xr.txLine; xr.ty = xr.tyLine;
        if (xr.result.Count > 0 && xr.result[^1].Text != "\r\n")
            xr.result.Add(new RawTextRun("\r\n", xr.tx, xr.ty, xr.fontSize, xr.currentFontName, 0, xr.ctm, xr.metrics));
        EnsureFontSet(xr, "'");
        if (xr.currentFontMissing) return;
        if (xr.operands.Count >= 1 && xr.operands[0] is PdfString s2)
        {
            var text2 = DecodeBytes(s2.Value, xr.toUnicode, xr.fontDict, xr.reader, xr.useFontEngineEncoding);
            var rawW2 = xr.metrics?.MeasureString(s2.Value, xr.fontSize) ?? 0;
            var nSp2 = text2.Count(c => c == ' ');
            var unscW2 = rawW2 + xr.charSpacing * text2.Length + xr.wordSpacing * nSp2;
            var w2 = unscW2 * xr.hScaling;
            xr.result.Add(new RawTextRun(text2, xr.tx, xr.ty, xr.fontSize, xr.currentFontName, unscW2, xr.ctm, xr.metrics,
                CharCumWidths: BuildCumWidthsForString(s2.Value, text2, xr.metrics, xr.fontSize, xr.charSpacing, xr.wordSpacing, unscW2),
                TmA: xr.tmA, TmB: xr.tmB, TmC: xr.tmC, TmD: xr.tmD, RenderingMode: xr.renderMode, LineWidth: xr.currentLineWidth,
                IsBold: xr.currentIsBold, IsItalic: xr.currentIsItalic, FontInfoObj: xr.currentFontInfo,
                HScaling: xr.hScaling, TextRise: xr.textRise, FillColor: xr.currentFillColor, StrokingColor: xr.currentStrokeColor,
                ClipRect: xr.currentClip, CharSpacing: xr.charSpacing, WordSpacing: xr.wordSpacing, TmBaseY: xr.tmBaseTy));
            xr.tx += xr.tmA * w2;
            xr.ty += xr.tmB * w2;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void ShowTextSpacedNextLineOp(ExtractRunsState xr)
    {
        // Set word spacing, char spacing, move to next line, show text
        if (xr.operands.Count >= 3)
        {
            xr.wordSpacing = GetNum(xr.operands[0]);
            xr.charSpacing = GetNum(xr.operands[1]);
        }
        xr.txLine = xr.tmA * 0 + xr.tmC * (-xr.leading) + xr.txLine;
        xr.tyLine = xr.tmB * 0 + xr.tmD * (-xr.leading) + xr.tyLine;
        xr.tx = xr.txLine; xr.ty = xr.tyLine;
        if (xr.result.Count > 0 && xr.result[^1].Text != "\r\n")
            xr.result.Add(new RawTextRun("\r\n", xr.tx, xr.ty, xr.fontSize, xr.currentFontName, 0, xr.ctm, xr.metrics));
        if (!xr.currentFontMissing && xr.operands.Count >= 3 && xr.operands[2] is PdfString s3)
        {
            var text3 = DecodeBytes(s3.Value, xr.toUnicode, xr.fontDict, xr.reader, xr.useFontEngineEncoding);
            var rawW3 = xr.metrics?.MeasureString(s3.Value, xr.fontSize) ?? 0;
            var nSp3 = text3.Count(c => c == ' ');
            var unscW3 = rawW3 + xr.charSpacing * text3.Length + xr.wordSpacing * nSp3;
            var w3 = unscW3 * xr.hScaling;
            xr.result.Add(new RawTextRun(text3, xr.tx, xr.ty, xr.fontSize, xr.currentFontName, unscW3, xr.ctm, xr.metrics,
                CharCumWidths: BuildCumWidthsForString(s3.Value, text3, xr.metrics, xr.fontSize, xr.charSpacing, xr.wordSpacing, unscW3),
                TmA: xr.tmA, TmB: xr.tmB, TmC: xr.tmC, TmD: xr.tmD, RenderingMode: xr.renderMode, LineWidth: xr.currentLineWidth,
                IsBold: xr.currentIsBold, IsItalic: xr.currentIsItalic, FontInfoObj: xr.currentFontInfo,
                HScaling: xr.hScaling, TextRise: xr.textRise, FillColor: xr.currentFillColor, StrokingColor: xr.currentStrokeColor,
                ClipRect: xr.currentClip, CharSpacing: xr.charSpacing, WordSpacing: xr.wordSpacing, TmBaseY: xr.tmBaseTy));
            xr.tx += xr.tmA * w3;
            xr.ty += xr.tmB * w3;
        }
    }
}
