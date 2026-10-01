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
    /// <summary>A single-segment fragment: word-wrapped at the content width into items of the current and following rows.</summary>
    private void LayoutSingleSegmentFragment(InlineCellLayoutState il, Aspose.Pdf.Text.TextFragment tf, double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign)
    {
        var text = tf.Text ?? string.Empty;
        var fs = ResolveFragmentFontSize(tf, defaultFontSize);
        var color = tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
        // Fragment-level embedded font AND CJK content: draw it as Type0/CID with the
        // font's real advances instead of the Standard-14 path (which would emit '?').
        // Scoped to CJK so an embedded Latin font keeps the existing inline path.
        var fragTtf = tf.TextState.Font?.SourceFontData?.TtfData;
        var cjk = fragTtf is not null && CjkCoveredBy(text, fragTtf);
        var runTtf = cjk ? fragTtf : null;
        var runName = cjk ? tf.TextState.Font?.FontName : null;
        double Wid(string t) => t.Length == 0 ? 0
            : runTtf is not null ? MeasureWidthWithFont(t, fs, runTtf)
            : MeasureWidthExact(t, fs);
        var itemH2 = RowH(il, fs);
        void Emit(string t, double w)
        {
            il.current.Add(new InlineItem
            {
                Text = t, FontSize = fs, Color = color, X = il.x, Width = w,
                Height = itemH2, BaseFontSize = fs, Ttf = runTtf, FontName = runName,
            });
            il.x += w;
            if (itemH2 > il.maxH) il.maxH = itemH2;
        }
        var whole = Wid(text);
        if (il.x + il.marginL + whole <= il.contentW)
        {
            // Fits at the pen: one run, as before.
            if (il.current.Count > 0 && il.x + il.marginL + whole > il.contentW) FlushInlineRow(il, cellAlign);
            il.x += il.marginL;
            Emit(text, whole);
        }
        else
        {
            // …otherwise it WRAPS. Emitting the whole paragraph as a single
            // run drew a 280-character cell as one line and clipped the rest
            // away at the cell edge; an inline fragment breaks at its tokens
            // like every other cell paragraph.
            if (il.current.Count > 0) FlushInlineRow(il, cellAlign);
            il.x += il.marginL;
            foreach (var token in SplitKeepingSpaces(text))
            {
                var core = token.TrimEnd(' ');
                var coreW = Wid(core);
                var tokW = Wid(token);
                if (il.current.Count > 0 && il.x + coreW > il.contentW) { FlushInlineRow(il, cellAlign); il.x += il.marginL; }
                if (coreW <= il.contentW)
                {
                    Emit(il.x + tokW <= il.contentW ? token : core,
                         il.x + tokW <= il.contentW ? tokW : coreW);
                    continue;
                }
                // A token wider than the cell fills character by character.
                var rest = token;
                while (rest.Length > 0)
                {
                    var take = 0; double tw2 = 0;
                    while (take < rest.Length)
                    {
                        var nw = Wid(rest.Substring(0, take + 1));
                        if (take > 0 && il.x + nw > il.contentW) break;
                        tw2 = nw; take++;
                    }
                    if (take == 0) { take = 1; tw2 = Wid(rest.Substring(0, 1)); }
                    Emit(rest.Substring(0, take), tw2);
                    rest = rest.Substring(take);
                    if (rest.Length > 0) { FlushInlineRow(il, cellAlign); il.x += il.marginL; }
                }
            }
        }
    }

    /// <summary>A multi-segment fragment: each segment becomes items at its own font, size and colour, wrapping at the content width.</summary>
    private void LayoutSegmentedFragment(InlineCellLayoutState il, Aspose.Pdf.Text.TextFragment tf, double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign)
    {
        il.x += il.marginL;
        foreach (var seg in il.segs)
        {
            if (!LayoutFragmentSegment(il, tf, seg, defaultFontSize, cellTextState, cellAlign)) break;
        }
    }

    /// <summary>Book one run of an inline segment as an item of the current row and advance the pen.</summary>
    private static void AddRun(InlineCellLayoutState il, string runText, double runW, byte[]? runTtf, string? runName,
        double runFs, double shiftY)
    {
        if (runTtf is not null) EnsureLineMarker(il, il.baseFs);
        else il.lineHasText = true;
        il.current.Add(new InlineItem
        {
            Text = runText, FontSize = runFs, Color = il.segColor, X = il.x, Width = runW,
            Height = il.itemH, BaseFontSize = il.baseFs, LineFontSize = il.lineFs, BaselineShift = shiftY,
            Ttf = runTtf, FontName = runName,
            Bold = il.segBold, Italic = il.segItalic, Underline = il.segUnderline,
        });
        il.x += runW;
        if (il.itemH > il.maxH) il.maxH = il.itemH;
    }

    /// <summary>One line-break piece of a segment: resolve its face, then place it whole when it fits, else token by token wrapped at the content width.</summary>
    private bool LayoutSegmentPiece(InlineCellLayoutState il, int spi, HorizontalAlignment cellAlign)
    {
        if (spi > 0) FlushInlineRow(il, cellAlign);
        il.segPiece = il.segPieces[spi];
        if (il.segPiece.Length == 0)
        {
            il.current.Add(new InlineItem { Text = "", Empty = true, FontSize = il.baseFs, Color = il.segColor, X = il.x, Width = 0, Height = RowH(il, il.baseFs) });
            if (RowH(il, il.baseFs) > il.maxH) il.maxH = RowH(il, il.baseFs);
            return true;
        }

        il.segTtf = il.ss.Font?.SourceFontData?.TtfData;
        il.segFontName = il.ss.Font?.FontName;
        // A NAMED face carries its own bold/italic siblings, and the
        // generator is what resolves them: TextState stores the flags
        // only for a fragment that belongs to no page (see
        // TextState.ApplyFontStyleToSource), leaving the writer to pick
        // the styled file. Without this a bold-italic Arial segment drew
        // in plain Arial.
        if (il.segTtf is not null && (il.segBold || il.segItalic))
        {
            var wantStyle = Aspose.Pdf.Text.FontStyles.Regular;
            if (il.segBold) wantStyle |= Aspose.Pdf.Text.FontStyles.Bold;
            if (il.segItalic) wantStyle |= Aspose.Pdf.Text.FontStyles.Italic;
            var family = Aspose.Pdf.Text.FontRepository.FamilyOf(
                il.ss.Font!.FontName ?? il.ss.Font.BaseFont);
            if (!string.IsNullOrEmpty(family))
            {
                Aspose.Pdf.Text.Font? styledFace = null;
                try { styledFace = Aspose.Pdf.Text.FontRepository.TryFindFont(family!, wantStyle, ignoreCase: true); }
                catch { }
                if (styledFace?.SourceFontData?.TtfData is { Length: > 12 } styledTtf)
                {
                    il.segTtf = styledTtf;
                    il.segFontName = styledFace.FontName;
                }
            }
        }
        il.isArabic = Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(il.segPiece);
        il.itemH = RowH(il, il.baseFs);
        il.sup = il.ss.Superscript;
        il.sub = il.ss.Subscript;

        // A piece the segment's own face covers and that fits whole at the
        // pen stays ONE run (one show op per segment).
        if (il.segTtf is not null && !NeedsGlyphFallback(il.segPiece, il.segTtf))
        {
            var whole = il.isArabic ? Aspose.Pdf.Text.ArabicTextShaper.Shape(il.segPiece) : il.segPiece;
            var fullW = MeasureWidthWithFont(whole, il.baseFs, il.segTtf);
            if (il.x + fullW <= il.contentW)
            {
                AddRun(il, whole, fullW, il.segTtf, il.segFontName, il.baseFs, 0);
                return true;
            }
        }
        // Standard-14 piece (sub/superscript at a reduced size with a
        // baseline shift) that needs no glyph fallback and fits: one run.
        if (il.segTtf is null && !NeedsGlyphFallback(il.segPiece, null))
        {
            var segFs = (il.sup || il.sub) ? il.baseFs * SubSuperScale : il.baseFs;
            var shift = il.sup ? il.baseFs * SuperscriptRise : il.sub ? -il.baseFs * SubscriptRise : 0.0;
            var sw = il.segBold ? MeasureFaceExact(il.segPiece, segFs, true)
                : MeasureWidthExact(il.segPiece, segFs);
            if (il.x + sw <= il.contentW || il.sup || il.sub)
            {
                if (il.current.Count > 0 && il.x + sw > il.contentW) { FlushInlineRow(il, cellAlign); il.x += il.marginL; }
                AddRun(il, il.segPiece, sw, null, null, segFs, shift);
                return true;
            }
        }
        // Otherwise the piece wraps token by token at the cell width. Each
        // token draws in the face that covers it — the segment's own, or a
        // glyph-covering substitute when that face lacks its script (Greek
        // and Arabic in a Standard-14 cell, ideographs in Arial) — and a
        // token wider than the cell is cut character by character.
        foreach (var token in SplitKeepingSpaces(il.segPiece))
        {
            if (!PlaceSegmentToken(il, token, cellAlign)) break;
        }
        return true;
    }
}
