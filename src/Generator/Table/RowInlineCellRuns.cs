using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// Per-segment run layout of a multi-segment inline fragment.
    // A line's first EMBEDDED-font run gets an empty marker run in the
    // default table font before it (a font-resource
    // prelude the absorber surfaces as an empty fragment). Standard-font
    // runs get no marker.
    private static void EnsureLineMarker(InlineCellLayoutState il, double fs)
    {
        if (il.lineHasText) return;
        il.lineHasText = true;
        il.current.Add(new InlineItem { Text = "", Empty = true, FontSize = fs, X = il.x, Width = 0, Height = RowH(il, fs) });
        if (RowH(il, fs) > il.maxH) il.maxH = RowH(il, fs);
    }

    /// <summary>One segment of a multi-segment fragment: its resolved face, size and style, split at line breaks, each piece run through the font's glyph coverage and wrapped at the content width.</summary>
    private bool LayoutFragmentSegment(InlineCellLayoutState il, Aspose.Pdf.Text.TextFragment tf, Aspose.Pdf.Text.TextSegment seg, double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign)
    {
        il.ss = seg.TextState;
        il.baseFs = il.ss.FontSizeTouched ? il.ss.FontSize
            : (tf.TextState.FontSizeTouched ? tf.TextState.FontSize : defaultFontSize);
        il.lineFs = tf.TextState.FontSizeTouched ? tf.TextState.FontSize
            : il.generatorPitch ? il.baseFs : defaultFontSize;
        il.segColor = il.ss.ForegroundColor ?? tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
        il.segBold = il.ss.IsBold || tf.TextState.IsBold || (cellTextState?.IsBold ?? false);
        il.segItalic = il.ss.IsItalic || tf.TextState.IsItalic;
        il.segUnderline = il.ss.Underline || tf.TextState.Underline;
        if (string.IsNullOrEmpty(seg.Text))
        {
            il.current.Add(new InlineItem { Text = "", Empty = true, FontSize = il.baseFs, Color = il.segColor, X = il.x, Width = 0, Height = RowH(il, il.baseFs) });
            if (RowH(il, il.baseFs) > il.maxH) il.maxH = RowH(il, il.baseFs);
            return true;
        }

        il.segPieces = seg.Text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var spi = 0; spi < il.segPieces.Length; spi++)
        {
            if (!LayoutSegmentPiece(il, spi, cellAlign)) break;
        }
        return true;
    }

    /// <summary>One token of a segment piece: its script runs measured with their faces, wrapped to a new row when the core does not fit, and booked run by run.</summary>
    private bool PlaceSegmentToken(InlineCellLayoutState il, string token, HorizontalAlignment cellAlign)
    {
        // A token is a chain of script runs, each in the face that
        // covers it (a Greek+ideograph token draws its Greek in the
        // Latin face and its ideographs in the CJK face).
        var runs = new List<(string text, byte[]? ttf, string? name)>();
        foreach (var piece in SplitScriptRuns(token))
        {
            var (pTtf, pName) = ResolveTokenFace(piece, il.ss.Font, il.segTtf, il.segFontName, il.faceCache);
            var pText = pTtf is not null && Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(piece)
                ? Aspose.Pdf.Text.ArabicTextShaper.Shape(piece) : piece;
            runs.Add((pText, pTtf, pName));
        }
        double W(string s, byte[]? ttf) => s.Length == 0 ? 0 : ttf is not null
            ? MeasureWidthWithFont(s, il.baseFs, ttf) : MeasureWidthExact(s, il.baseFs);
        double tw = 0;
        foreach (var rn in runs) tw += W(rn.text, rn.ttf);
        // The fit test ignores the token's trailing space: a word that
        // fits stays on the line and keeps its space only when the space
        // fits too (otherwise the space is dropped with the break).
        var lastRun = runs[^1];
        var lastCore = lastRun.text.TrimEnd(' ');
        var coreW = tw - W(lastRun.text, lastRun.ttf) + W(lastCore, lastRun.ttf);
        if (il.current.Count > 0 && il.x + coreW > il.contentW) { FlushInlineRow(il, cellAlign); il.x += il.marginL; }
        if (coreW <= il.contentW)
        {
            var keepSpace = il.x + tw <= il.contentW;
            for (var rIdx = 0; rIdx < runs.Count; rIdx++)
            {
                var (rt, rTtf, rName) = runs[rIdx];
                if (rIdx == runs.Count - 1 && !keepSpace) rt = lastCore;
                if (rt.Length == 0) continue;
                AddRun(il, rt, W(rt, rTtf), rTtf, rName, il.baseFs, 0);
            }
            return true;
        }
        // Wider than the cell: character fill across the runs.
        foreach (var (rt, rTtf, rName) in runs)
        {
            var rest = rt;
            while (rest.Length > 0)
            {
                var take = 0;
                double pw = 0;
                while (take < rest.Length)
                {
                    var len = char.IsHighSurrogate(rest[take]) && take + 1 < rest.Length
                              && char.IsLowSurrogate(rest[take + 1]) ? 2 : 1;
                    var cw = W(rest.Substring(take, len), rTtf);
                    if (take > 0 && il.x + pw + cw > il.contentW) break;
                    pw += cw;
                    take += len;
                }
                // A run whose first glyph no longer fits moves to the next line.
                if (il.current.Count > 0 && il.x + pw > il.contentW) { FlushInlineRow(il, cellAlign); il.x += il.marginL; continue; }
                AddRun(il, rest.Substring(0, take), pw, rTtf, rName, il.baseFs, 0);
                rest = rest.Substring(take);
                if (rest.Length > 0) { FlushInlineRow(il, cellAlign); il.x += il.marginL; }
            }
        }
        return true;
    }
}
