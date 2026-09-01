using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The block parser's Flush, lifted out of ParseBlocks: it closes the block
// under construction from the parse state and the rhythm flags. Body is verbatim.
    private static void Flush(ParseBlocksState pb, bool controlBoxes, bool uaBlockRhythm, bool articleRhythm, bool spanPtTypography, bool _unused, BlockStyle styleUsed)
    {
        // An <a> still open at the flush boundary covers text up to here in THIS block.
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
        var raw = pb.currentText.ToString();
        // Collapse runs of *ASCII* whitespace only — U+00A0 (from
        // &nbsp;) is intentional visual content and must survive
        // collapse+Trim so an &nbsp;-only <p> still emits a line.
        // CollapseWhitespaceWithMap reproduces that collapse+Trim while tracking,
        // for each output char, the raw index it came from — so inline anchor
        // spans can be re-expressed in the collapsed Text's coordinates.
        var (collapsed, rawOf) = CollapseWhitespaceWithMap(raw);
        // Text continuing an inline run after a control keeps the one collapsed
        // space the markup put between them — " State: " draws
        // with its leading space right at the control's edge.
        if (controlBoxes && pb.inlineRunId != 0 && pb.runPrevWasControl
            && collapsed.Length > 0 && raw.Length > 0 && char.IsWhiteSpace(raw[0]))
        {
            collapsed = " " + collapsed;
            rawOf.Insert(0, -1);
        }
        // A collapsed newline before a <br> survives as the fragment's trailing
        // space (quirks CSS-run docs — the flush the <br> triggers sets the
        // marker; asserted fragment values carry it).
        if (pb.keepTrailingSpace && collapsed.Length > 0 && raw.Length > 0
            && char.IsWhiteSpace(raw[^1]) && collapsed[^1] != ' ')
        {
            collapsed += " ";
            rawOf.Add(raw.Length - 1);
        }
        // font-size:0 spacer (the float-terminator "clear:both;height:0;
        // font-size:0" idiom): its &nbsp; occupies a zero-height line box —
        // emit nothing rather than a default-size blank line.
        if (styleUsed.ZeroFontSize && collapsed.Trim(' ', ' ').Length == 0)
        {
            pb.currentText.Clear();
            pb.rawAnchors.Clear();
            pb.rawBolds.Clear();
            pb.rawUnders.Clear();
            pb.rawItalics.Clear();
            pb.rawColorRuns.Clear();
            pb.rawDecorRuns.Clear();
            return;
        }
        if (collapsed.Length > 0)
        {
            var blk = new Block
            {
                Text = collapsed,
                FontSize = styleUsed.FontSize,
                LeadFontSize = pb.ptyLeadFs > 0 && pb.ptyLeadFs != styleUsed.FontSize
                    ? pb.ptyLeadFs : 0,
                RightInsetPt = styleUsed.RightInsetPt,
                SmallCaps = styleUsed.SmallCaps,
                TextIndentPt = styleUsed.TextIndentPt,
                LetterSpacingPt = styleUsed.LetterSpacingPt,
                FontRes = styleUsed.FontRes,
                FontFamily = styleUsed.FontFamily,
                ForeColor = styleUsed.ForeColor,
                LegacyFontPt = styleUsed.LegacyFontPt,
                LegacyFontSized = styleUsed.LegacyFontSized,
                EmBold = styleUsed.EmBold,
                EmItalic = styleUsed.EmItalic,
                MarginTop = styleUsed.MarginTop,
                MarginBottom = styleUsed.MarginBottom,
                MarginTopAlways = styleUsed.MarginTopAlways,
                MarginTopAuthored = styleUsed.MarginTopAuthored,
                LeftIndent = styleUsed.LeftIndent,
                IsListItem = styleUsed.IsListItem,
                PageBreakBefore = styleUsed.PageBreakBefore || pb.pendingPageBreak,
                // An element's declared HEIGHT reserves space for the WHOLE
                // element, so it belongs to the line that CLOSES it: a <p> 200px
                // tall holding four <br>-separated lines pads once, after the
                // fourth, where charging the first spread them a box apart.
                ExplicitHeight = pb.closingElement ? styleUsed.ExplicitHeight : 0,
                ShorthandLeftPt = styleUsed.ShorthandLeftPt,
                ShorthandTopPt = styleUsed.ShorthandTopPt,
                MarginRightPt = styleUsed.MarginRightPt,
                FontFamilyStack = styleUsed.FontFamilyStack,
                DeclaredWidthPt = styleUsed.DeclaredWidthPt,
                LineFactor = styleUsed.LineFactor,
                DeclaredLineFactor = styleUsed.DeclaredLineFactor,
                BackgroundColor = styleUsed.BackgroundColor,
                BandPadPt = styleUsed.BandPadPt,
                BgPadTopPt = styleUsed.BgPadTopPt,
                BgPadBottomPt = styleUsed.BgPadBottomPt,
                BgPadLeftPt = styleUsed.BgPadLeftPt,
                BgBoxWidthPt = styleUsed.BgBoxWidthPt,
                BgBoxHeightPt = styleUsed.BgBoxHeightPt,
                BorderColor = styleUsed.BorderColor,
                BorderTopOnly = styleUsed.BorderTopOnly,
                BorderWidth = styleUsed.BorderWidth,
                LineBoxPt = styleUsed.LineBoxPt,
                TextInsetPt = styleUsed.TextInsetPt,
                AlignCenter = styleUsed.AlignCenter,
                AlignCenterCss = styleUsed.AlignCenterCss,
                AlignJustify = styleUsed.AlignJustify,
                AlignCenterAttr = styleUsed.AlignCenterAttr,
                WidthFrac = styleUsed.WidthFrac,
                WidthPx = styleUsed.WidthPx,
                PadTop = styleUsed.PadTop,
                AlignRight = styleUsed.AlignRight,
                BandColor = styleUsed.BandColor,
                BandPx = styleUsed.BandPx,
                BandPadPx = styleUsed.BandPadPx,
                FloatLeft = styleUsed.FloatLeft,
                FloatRight = styleUsed.FloatRight,
            };
            // A symbol span's face must not become the whole BLOCK's family —
            // its PUA chars carry their own face at draw; the block text is
            // the document serif (redline dialect).
            if (spanPtTypography && blk.FontFamily is "Wingdings" or "Webdings")
                blk.FontFamily = "Times New Roman";
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
            if (pb.pendingColIndent > 0 && !blk.IsHardBreak)
            {
                blk.LeftIndent += pb.pendingColIndent;
                pb.pendingColIndent = 0;
            }
            // A painted box (background tile × declared size) fills once, on
            // the element's first flushed block; its fill dies with it.
            if (styleUsed.BgBoxHeightPt > 0)
            {
                styleUsed.BgBoxWidthPt = 0;
                styleUsed.BgBoxHeightPt = 0;
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
            foreach (var (ds0, de0, dk0, dc0) in pb.rawDecorRuns)
            {
                int ds = -1;
                int de = -1;
                for (int k = 0; k < rawOf.Count; k++)
                    if (rawOf[k] >= ds0 && rawOf[k] < de0) { if (ds < 0) ds = k; de = k + 1; }
                if (ds >= 0) (blk.DecorRuns ??= new()).Add((ds, de - ds, dk0, dc0));
            }
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
        else if (styleUsed.ExplicitHeight > 0 && !styleUsed.HeightFloorDeferred)
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
        // Empty block close-tags without explicit height do not emit a
        // spacer — nested empty containers (e.g. <div><div></div></div>)
        // would otherwise inflate page count well beyond the text
        // volume. Explicit vertical spacing comes from <br>, <hr>,
        // block margins, and any CSS height/min-height override.
        pb.currentText.Clear();
        pb.ptyLeadFs = 0;
        pb.rawDecorRuns.Clear();
        pb.rawAnchors.Clear();
        pb.rawBolds.Clear();
        pb.rawUnders.Clear();
        pb.rawItalics.Clear();
        pb.rawColorRuns.Clear();
        // An <a> left open across a block/line boundary continues in the next
        // block; record what it covered here and re-anchor it at offset 0.
        if (pb.openAnchors.Count > 0)
        {
            var carried = pb.openAnchors.ToArray();
            pb.openAnchors.Clear();
            for (int oi = carried.Length - 1; oi >= 0; oi--)
                pb.openAnchors.Push((0, carried[oi].url));
        }
    }
}
