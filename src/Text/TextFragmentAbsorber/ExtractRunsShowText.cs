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
            ShowTextArray(xr, arr);
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
