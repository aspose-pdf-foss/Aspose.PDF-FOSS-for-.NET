using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An empty block with an explicit height still books its height floor as a block.</summary>
    private static void EmitHeightFloorBlock(ParseBlocksState pb, BlockStyle styleUsed)
    {
        // Empty block with explicit height (e.g. `<div style="height:50px">`
        // used as a visual separator bar). Emit a text-less spacer
        // so pagination sees the reserved vertical space — once: an inner
        // <br> flush and the element's close both land here otherwise.
        // A container whose floor is still OPEN skips this: its height is
        // space its own content grows INTO, spent by the floor markers at
        // the element's close, not reserved ahead of the content.
        pb.blocks.Add(new Block
        {
            Text = "",
            FontSize = styleUsed.FontSize,
            FontRes = styleUsed.FontRes,
            MarginTop = 0,
            MarginBottom = 0,
            LeftIndent = styleUsed.LeftIndent,
            IsHardBreak = true,
            ExplicitHeight = styleUsed.ExplicitHeight,
        });
        styleUsed.ExplicitHeight = 0;
    }

    /// <summary>The flushed text becomes a block: its style, runs, anchors and decorations mapped from the raw text onto the collapsed one, with the dialect's rhythm and spacing rules.</summary>
    private static void EmitFlushedBlock(ParseBlocksState pb, bool controlBoxes, bool uaBlockRhythm, bool articleRhythm, bool spanPtTypography, BlockStyle styleUsed, string collapsed, List<int> rawOf)
    {
        var blk = NewFlushedBlock(pb, styleUsed, collapsed);
        // A symbol span's face must not become the whole BLOCK's family —
        // its PUA chars carry their own face at draw; the block text is
        // the document serif (redline dialect).
        ApplyPendingBlockSpacing(pb, uaBlockRhythm, spanPtTypography, styleUsed, blk);
        MapBlockRuns(pb, articleRhythm, rawOf, blk);
        if (pb.pendingAnchorNames.Count > 0)
        {
            blk.AnchorNames = new List<string>(pb.pendingAnchorNames);
            pb.pendingAnchorNames.Clear();
        }
        if (controlBoxes && pb.inlineRunId != 0)
        {
            blk.InlineRunId = pb.inlineRunId;
            pb.runPrevWasControl = false;
        }
        if (pb.pendingInlineIcon)
        {
            blk.InlineIconAfter = true;
            pb.pendingInlineIcon = false;
        }
        // The element chain's pending padding-top LONGHAND lands on the
        // first block that flushes (cascaded down the opens above).
        if (styleUsed.OwnPadTopPt > 0)
        {
            blk.PadTop += styleUsed.OwnPadTopPt;
            styleUsed.OwnPadTopPt = 0;
        }
        pb.blocks.Add(blk);
        pb.pendingPageBreak = false;
        if (pb.closingElement) styleUsed.ExplicitHeight = 0;
    }

    /// <summary>Close the anchors, bold, underline, italic, colour and decoration runs still open at the flush.</summary>
    private static void CloseInlineRuns(ParseBlocksState pb)
    {
        foreach (var oa in pb.openAnchors)
            pb.rawAnchors.Add((oa.start, pb.currentText.Length, oa.url));
        // A bold run still open at the flush boundary covers text up to here and
        // re-opens at the start of the next block.
        if (pb.inlineBoldDepth > 0 && pb.currentText.Length > pb.inlineBoldStart && pb.inlineBoldStart >= 0)
            pb.rawBolds.Add((pb.inlineBoldStart, pb.currentText.Length));
        if (pb.inlineBoldDepth > 0) pb.inlineBoldStart = 0;
        if (pb.inlineUnderDepth > 0 && pb.currentText.Length > pb.inlineUnderStart && pb.inlineUnderStart >= 0)
            pb.rawUnders.Add((pb.inlineUnderStart, pb.currentText.Length));
        if (pb.inlineUnderDepth > 0) pb.inlineUnderStart = 0;
        if (pb.inlineItalicDepth > 0 && pb.currentText.Length > pb.inlineItalicStart && pb.inlineItalicStart >= 0)
            pb.rawItalics.Add((pb.inlineItalicStart, pb.currentText.Length));
        if (pb.inlineItalicDepth > 0) pb.inlineItalicStart = 0;
        // Colour runs still open at the flush boundary cover text up to
        // here and re-open at the start of the next block.
        if (pb.openColorRuns.Count > 0)
        {
            foreach (var ocr in pb.openColorRuns)
                if (pb.currentText.Length > ocr.start)
                    pb.rawColorRuns.Add((ocr.start, pb.currentText.Length, ocr.c));
            var reopen = pb.openColorRuns.ToArray();
            pb.openColorRuns.Clear();
            for (var oi = reopen.Length - 1; oi >= 0; oi--)
                pb.openColorRuns.Push((reopen[oi].depth, 0, reopen[oi].c, reopen[oi].prev));
        }
        if (pb.openSizeRuns.Count > 0)
        {
            foreach (var osr in pb.openSizeRuns)
                if (pb.currentText.Length > osr.start)
                    pb.rawSizeRuns.Add((osr.start, pb.currentText.Length, osr.pt));
            var sreopen = pb.openSizeRuns.ToArray();
            pb.openSizeRuns.Clear();
            for (var oi = sreopen.Length - 1; oi >= 0; oi--)
                pb.openSizeRuns.Push((sreopen[oi].depth, 0, sreopen[oi].pt));
        }
        if (pb.openFamilyRuns.Count > 0)
        {
            foreach (var ofr in pb.openFamilyRuns)
                if (pb.currentText.Length > ofr.start)
                    pb.rawFamilyRuns.Add((ofr.start, pb.currentText.Length, ofr.fam));
            var freopen = pb.openFamilyRuns.ToArray();
            pb.openFamilyRuns.Clear();
            for (var oi = freopen.Length - 1; oi >= 0; oi--)
                pb.openFamilyRuns.Push((freopen[oi].depth, 0, freopen[oi].fam));
        }
        if (pb.openDecorRuns.Count > 0)
        {
            foreach (var odr in pb.openDecorRuns)
                if (pb.currentText.Length > odr.start)
                    pb.rawDecorRuns.Add((odr.start, pb.currentText.Length, odr.kind, odr.c));
            var dreopen = pb.openDecorRuns.ToArray();
            pb.openDecorRuns.Clear();
            for (var oi = dreopen.Length - 1; oi >= 0; oi--)
                pb.openDecorRuns.Push((dreopen[oi].depth, 0, dreopen[oi].kind, dreopen[oi].c));
        }
    }

    /// <summary>Map the pending marker, anchors, bold/underline/italic, colour and decoration runs from raw offsets onto the collapsed text.</summary>
    private static void MapBlockRuns(ParseBlocksState pb, bool articleRhythm, List<int> rawOf, Block blk)
    {
        if (pb.pendingMarker is not null)
        {
            // Styled-article: "• item" draws as ONE run
            // starting AT the list indent — the marker rides the text,
            // it does not hang left of it.
            if (articleRhythm && !pb.pendingMarkerAfter)
                blk.Text = pb.pendingMarker + " " + blk.Text;
            else
            {
                blk.Marker = pb.pendingMarker;
                blk.MarkerAfter = pb.pendingMarkerAfter;
            }
            pb.pendingMarker = null;
            pb.pendingMarkerAfter = false;
        }
        if (pb.rawAnchors.Count > 0)
        {
            foreach (var (s, e, url) in pb.rawAnchors)
            {
                if (string.IsNullOrEmpty(url)) continue;
                int cs = -1;
                int ce = -1;
                for (int k = 0; k < rawOf.Count; k++)
                    if (rawOf[k] >= s && rawOf[k] < e) { if (cs < 0) cs = k; ce = k + 1; }
                if (cs >= 0)
                    (blk.Anchors ??= new()).Add((cs, ce - cs, url));
            }
        }
        // Re-express a raw-coordinate emphasis range in the collapsed Text's
        // coordinates: the chars the collapse kept whose source index falls
        // inside the range span [first, last].
        void MapRuns(List<(int start, int end)> src,
            Func<System.Collections.Generic.List<(int Start, int Length)>> target)
        {
            foreach (var (s, e) in src)
            {
                int cs = -1;
                int ce = -1;
                for (int k = 0; k < rawOf.Count; k++)
                    if (rawOf[k] >= s && rawOf[k] < e) { if (cs < 0) cs = k; ce = k + 1; }
                if (cs >= 0) target().Add((cs, ce - cs));
            }
        }
        if (pb.rawBolds.Count > 0) MapRuns(pb.rawBolds, () => blk.BoldRuns ??= new());
        if (pb.rawUnders.Count > 0) MapRuns(pb.rawUnders, () => blk.UnderlineRuns ??= new());
        if (pb.rawStrikes.Count > 0) MapRuns(pb.rawStrikes, () => blk.StrikeRuns ??= new());
        if (pb.rawItalics.Count > 0) MapRuns(pb.rawItalics, () => blk.ItalicRuns ??= new());
        foreach (var (cs0, ce0, cc0) in pb.rawColorRuns)
        {
            int cs = -1;
            int ce = -1;
            for (int k = 0; k < rawOf.Count; k++)
                if (rawOf[k] >= cs0 && rawOf[k] < ce0) { if (cs < 0) cs = k; ce = k + 1; }
            if (cs < 0) continue;
            // An INNER span's colour beats the wrapper's over the same
            // range (the runs close inner-first, so the first entry with
            // a given extent is the innermost — keep it).
            var dup = false;
            if (blk.ColorRuns is not null)
                foreach (var ex in blk.ColorRuns)
                    if (ex.Start == cs && ex.Length == ce - cs) { dup = true; break; }
            if (!dup) (blk.ColorRuns ??= new()).Add((cs, ce - cs, cc0));
        }
        // One colour run wrapping the whole block IS the block's ink —
        // the calibrated block-level path (a fully-wrapped paragraph)
        // keeps working; partial runs stay run-scoped.
        if (blk.ForeColor is null && blk.ColorRuns is [{ Start: <= 0 } single]
            && single.Length >= blk.Text.TrimEnd(' ').Length)
        {
            blk.ForeColor = single.C;
            blk.ColorRuns = null;
        }
        foreach (var (ss0, se0, sp0) in pb.rawSizeRuns)
        {
            int ss = -1;
            int se = -1;
            for (int k = 0; k < rawOf.Count; k++)
                if (rawOf[k] >= ss0 && rawOf[k] < se0) { if (ss < 0) ss = k; se = k + 1; }
            if (ss >= 0) (blk.SizeRuns ??= new()).Add((ss, se - ss, sp0));
        }
        foreach (var (fs0, fe0, ff0) in pb.rawFamilyRuns)
        {
            int fs = -1;
            int fe = -1;
            for (int k = 0; k < rawOf.Count; k++)
                if (rawOf[k] >= fs0 && rawOf[k] < fe0) { if (fs < 0) fs = k; fe = k + 1; }
            if (fs >= 0) (blk.FamilyRuns ??= new()).Add((fs, fe - fs, ff0));
        }
        if (pb.wordExport) TakeWholeBlockRuns(blk);
        foreach (var (ds0, de0, dk0, dc0) in pb.rawDecorRuns)
        {
            int ds = -1;
            int de = -1;
            for (int k = 0; k < rawOf.Count; k++)
                if (rawOf[k] >= ds0 && rawOf[k] < de0) { if (ds < 0) ds = k; de = k + 1; }
            if (ds >= 0) (blk.DecorRuns ??= new()).Add((ds, de - ds, dk0, dc0));
        }
    }

    /// <summary>Spend the pending empty-paragraph margin, column indent, background box, box padding, border box and UA rhythm on the new block.</summary>
    private static void ApplyPendingBlockSpacing(ParseBlocksState pb, bool uaBlockRhythm, bool spanPtTypography, BlockStyle styleUsed, Block blk)
    {
        if (spanPtTypography && blk.FontFamily is "Wingdings" or "Webdings")
            blk.FontFamily = "Times New Roman";
        // The sheet-typography flow draws every block in a face it can measure and embed: a
        // stack whose first face is not installed ("Segoe UI Semilight", Segoe UI, ...) draws
        // in its first drawable one, and a face-less block in the body's.
        if (pb.sheetElementTypography && !IsDrawableFace(blk.FontFamily))
            blk.FontFamily = ResolvableFamily(blk.FontFamilyStack)
                ?? (IsDrawableFace(pb.sheetBodyFace) ? pb.sheetBodyFace : blk.FontFamily);
        // An empty paragraph's margin max-collapses onto this block.
        if (pb.pendingEmptyPMarginPt > 0)
        {
            blk.MarginTop = Math.Max(blk.MarginTop, pb.pendingEmptyPMarginPt);
            pb.pendingEmptyPMarginPt = 0;
        }
        // The div's padding-top spaces only its FIRST flushed block;
        // a heading band draws once, under the first flushed block.
        styleUsed.PadTop = 0;
        styleUsed.BandColor = null;
        // The run following a title column seats at the column's right edge
        // on the SAME line (the title block gave its row back).
        if ((pb.pendingColIndent > 0 || pb.pendingColIndentFrac > 0) && !blk.IsHardBreak)
        {
            blk.LeftIndent += pb.pendingColIndent;
            blk.LeftIndentFrac += pb.pendingColIndentFrac;
            // (a field value takes the label's column share alone: the label spent the row's margin)
            if (pb.cv?.profile.fieldListDoc == true) { blk.FieldColumnFrac = pb.pendingColIndentFrac; blk.LeftIndentFrac = 0; blk.MarginTop = 0; }
            pb.pendingColIndent = 0;
            pb.pendingColIndentFrac = 0;
        }
        // A painted box (background tile × declared size) fills once, on
        // the element's first flushed block; its fill dies with it.
        if (styleUsed.BgBoxHeightPt > 0 || styleUsed.BgBoxHeightVh > 0)
        {
            styleUsed.BgImageSrc = null;
            styleUsed.BgImageSize = "";
            styleUsed.BgBoxWidthPt = 0;
            styleUsed.BgBoxHeightPt = 0;
            styleUsed.BgBoxHeightVh = 0;
            styleUsed.BackgroundColor = null;
        }
        // Container chrome above this block, and a container's class-rule
        // height flooring it (containerBoxIndents mode) — both one-shot.
        if (pb.pendingBoxPadTop > 0)
        {
            blk.MarginTop += pb.pendingBoxPadTop;
            blk.MarginTopAlways = true;
            pb.pendingBoxPadTop = 0;
        }
        if (pb.pendingBoxHeight > 0)
        {
            blk.ExplicitHeight = Math.Max(blk.ExplicitHeight, pb.pendingBoxHeight);
            blk.BandBoxHeight = true;
            pb.pendingBoxHeight = 0;
        }
        // The border-only declared box lands on the element's first flushed
        // block: the box strokes at that block's top and the content height
        // reserves the flow below its lines.
        if (pb.pendingBorderBox is { } pbb)
        {
            blk.BorderBoxWPt = pbb.w;
            blk.BorderRadiusPt = pbb.r;
            blk.BorderWidth = pbb.bw;
            blk.BorderColor = pbb.c;
            blk.ExplicitHeight = Math.Max(blk.ExplicitHeight, pbb.h);
            pb.pendingBorderBox = null;
        }
        // A block's margins belong to the BOX, not to each line a <br> splits
        // off inside it: the top margin is spent on the first flushed line and
        // the bottom margin is re-attached when the element closes.
        if (uaBlockRhythm)
        {
            styleUsed.MarginTop = 0;
            blk.MarginBottom = 0;
        }
        // Attach a pending list marker to this first content block of the <li>.
    }
}
