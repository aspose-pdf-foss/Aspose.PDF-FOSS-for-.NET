using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A laid-out box: its border box in continuous page space (y from the sheet top,
    /// pages cut later), its lines, its child boxes in document order.</summary>
    private sealed class FbBox
    {
        public HtmlNode? Node;
        public FbStyle St = null!;
        public FbBoxProps P = null!;
        public double X, Y, W, H;                              // border box
        public List<FbBox> Kids = new();
        public List<FbLine> Lines = new();
        public bool IsBfc, IsFloat, IsTable, IsInline;         // IsInline: an inline-block or control seated on a line
        public FbTableGrid? Grid;                              // a table's grid geometry, for the borders
        public FbControl? Ctl;                                 // a form control's widget
        public FbReplaced? Img;                                // a picture that could not load, boxed
        public double ContentX => X + P.Border.L + P.Pad.L;
        public double ContentY => Y + P.Border.T + P.Pad.T;
        public double ContentW => W - P.ChromeH;
        public double OuterW => W + P.Margin.H;
        public double OuterH => H + P.Margin.V;
        public bool HasBlockKids
        {
            get { foreach (var k in Kids) if (!k.IsFloat && !k.IsInline) return true; return false; }
        }
    }

    /// <summary>The floats of one block formatting context, as margin boxes.</summary>
    private sealed class FbBfc
    {
        public List<(double x0, double x1, double y0, double y1, bool left)> Floats = new();
        public double Bottom()
        {
            var b = 0.0;
            foreach (var f in Floats) b = Math.Max(b, f.y1);
            return b;
        }
        /// <summary>The free segment at a height: from the right edge of the left floats crossing it
        /// to the left edge of the right floats crossing it.</summary>
        public (double x0, double x1) Segment(double y, double cbX, double cbW)
        {
            double x0 = cbX, x1 = cbX + cbW;
            foreach (var f in Floats)
            {
                if (y < f.y0 - FbEpsilon || y >= f.y1 - FbEpsilon) continue;
                if (f.left) x0 = Math.Max(x0, f.x1);
                else x1 = Math.Min(x1, f.x0);
            }
            return (x0, x1);
        }
        /// <summary>The next float bottom below a height (the next line where the segment changes).</summary>
        public double NextBottom(double y)
        {
            var next = double.MaxValue;
            foreach (var f in Floats)
                if (f.y1 > y + FbEpsilon) next = Math.Min(next, f.y1);
            return next;
        }
    }

    /// <summary>The walk state of one container: the pending inline run and the bottom margin of
    /// the last block, not yet spent (it collapses with the next block's top margin).</summary>
    private sealed class FbFlow
    {
        public List<FbItem> Run = new();
        public double PendingMb;
        public double Collapsed;                               // the margin already spent above the container's top, which its first content's margin collapses into
    }

    /// <summary>Lay a container's children out at its content box: text and inline elements gather
    /// into a run that flushes as lines, a block child flushes the run and stacks (adjacent block
    /// margins collapse to the larger), a floated child is placed on the run's line beside the
    /// earlier floats; the last block's bottom margin ends the content.</summary>
    private static void FbLayoutChildren(FbState fb, FbBox parent, HtmlNode el, FbStyle st, double cx, double cw, ref double cursor, FbBfc bfc, double collapsedTop = 0)
    {
        var flow = new FbFlow { Collapsed = collapsedTop };
        FbWalkInline(fb, parent, el, st, cx, cw, ref cursor, bfc, flow);
        FbFlushFlow(fb, parent, flow, cx, cw, ref cursor, bfc);
        cursor += flow.PendingMb;
        flow.PendingMb = 0;
    }

    /// <summary>Spend the pending run as lines (after the pending block margin).</summary>
    private static void FbFlushFlow(FbState fb, FbBox parent, FbFlow flow, double cx, double cw, ref double cursor, FbBfc bfc)
    {
        if (flow.Run.Count == 0) return;
        if (FbRunHasContent(flow.Run)) { cursor += Math.Max(0, flow.PendingMb - flow.Collapsed); flow.PendingMb = 0; flow.Collapsed = 0; }
        FbFlushRun(fb, parent, flow.Run, cx, cw, ref cursor, bfc);
    }

    private static void FbWalkInline(FbState fb, FbBox parent, HtmlNode el, FbStyle st, double cx, double cw, ref double cursor, FbBfc bfc, FbFlow flow)
    {
        FbInsertGenerated(fb, el);
        foreach (var c in el.Children)
        {
            if (c.Tag.Length == 0) { FbAddText(fb, flow.Run, c.Text, st); continue; }
            if (c.Tag is "script" or "style" or "head" or "title" or "meta" or "link" or "colgroup" or "col" or "option" or "noscript") continue;
            if (c.Tag == "br") { flow.Run.Add(FbItem.Break()); continue; }
            var cst = FbStyleOf(fb, c, st);
            var p = FbPropsOf(fb, c, cw, parent.P.HeightAuto ? 0 : parent.P.HeightPt, cst.Px);
            if (p.Display == "none") continue;
            if (c.Tag == "img")
            {
                FbAddPicture(fb, parent, c, cst, p, cx, cw, ref cursor, bfc, flow);
                continue;
            }
            // (quirks: a paragraph without a margin of its own has the UA 1.12 em)
            if (fb.quirks && c.Tag == "p" && !p.MarginDeclared) p.Margin.T = p.Margin.B = FbUaParagraphMarginEm * cst.Pt;
            if (p.Float != "none")
            {
                cursor += flow.PendingMb;
                flow.PendingMb = 0;
                FbPlaceFloat(fb, parent, c, cst, p, cx, cw, cursor, bfc);
                continue;
            }
            if (c.Tag is "input" or "textarea" or "select" or "button")
            {
                if (FbControlItem(fb, parent, c, cst, p) is { } ctl) flow.Run.Add(ctl);
                continue;
            }
            var block = c.Tag == "table" ? p.Display != "inline" && p.Display != "inline-block" : FbIsBlock(c, p);
            if (block)
            {
                FbLayoutBlockChild(fb, parent, c, cst, p, cx, cw, ref cursor, bfc, flow);
                continue;
            }
            if (p.Display == "inline-block")
            {
                flow.Run.Add(FbInlineBoxItem(fb, parent, c, cst, p, cw));
                continue;
            }
            if (p.Margin.L > 0) flow.Run.Add(FbItem.Spacer(p.Margin.L, st));
            if (p.BgRgb is not null) cst.BgRgb = p.BgRgb;      // an inline element's background paints behind its runs
            FbWalkInline(fb, parent, c, cst, cx, cw, ref cursor, bfc, flow);
            if (p.Margin.R > 0) flow.Run.Add(FbItem.Spacer(p.Margin.R, st));
        }
    }

    /// <summary>An in-flow block child: the run before it flushes, a clear steps past the floats,
    /// its top margin collapses with the pending bottom margin, an empty box's margins collapse
    /// through, and a line-level box that misses the page band moves whole to the next sheet.</summary>
    private static void FbLayoutBlockChild(FbState fb, FbBox parent, HtmlNode c, FbStyle cst, FbBoxProps p, double cx, double cw, ref double cursor, FbBfc bfc, FbFlow flow)
    {
        FbFlushFlow(fb, parent, flow, cx, cw, ref cursor, bfc);
        // (quirks, probed: the gap above a block is the largest adjoining margin - its own top, the
        //  pending bottom, and the margin its container already spent above ITS top when nothing
        //  separates them; a plain block passes what it spent on to its first content)
        var gap = FbCollapsedMargin(flow.PendingMb, p.Margin.T);
        var top = cursor + (gap < 0 ? gap : Math.Max(0, gap - flow.Collapsed));
        // clearance: the border top lands below the floats it clears, margin and all
        if (p.Clear != "none") top = Math.Max(top, FbClearBottom(bfc, p.Clear));
        var passedOn = fb.quirks && p.Border.T <= 0 && p.Pad.T <= 0 ? Math.Max(gap, flow.Collapsed) : 0;
        var floatsBefore = bfc.Floats.Count;
        var box = c.Tag == "table" ? FbLayoutTable(fb, c, cst, p, cx, cw, top) : FbLayoutBlock(fb, c, cst, p, cx, cw, top, bfc, p.Bfc, passedOn);
        if (box.H <= FbEpsilon && !p.HasVisibleBorder && p.Pad.V <= 0 && box.Lines.Count == 0 && box.Kids.Count == 0)
        {
            // an empty block: its own margins collapse together and with its neighbours
            flow.PendingMb = Math.Max(flow.PendingMb, Math.Max(p.Margin.T, p.Margin.B));
            box.Y = cursor;
            parent.Kids.Add(box);
            return;
        }
        var pageBottom = FbPageBottom(fb, FbPageOf(fb, top).page);
        if (!box.HasBlockKids && box.Y < pageBottom - FbEpsilon && box.Y + box.H > pageBottom + FbEpsilon && box.H <= fb.laterPageStep)
        {
            bfc.Floats.RemoveRange(floatsBefore, bfc.Floats.Count - floatsBefore);
            box = c.Tag == "table" ? FbLayoutTable(fb, c, cst, p, cx, cw, pageBottom) : FbLayoutBlock(fb, c, cst, p, cx, cw, pageBottom, bfc, p.Bfc, passedOn);
        }
        parent.Kids.Add(box);
        cursor = box.Y + box.H;
        flow.PendingMb = p.Margin.B;
        flow.Collapsed = 0;
    }

    /// <summary>Two adjoining vertical margins collapsed (CSS 2.1): the larger of two positives, the more
    /// negative of two negatives, the sum of a positive and a negative.</summary>
    private static double FbCollapsedMargin(double a, double b)
        => a >= 0 && b >= 0 ? Math.Max(a, b) : a < 0 && b < 0 ? Math.Min(a, b) : a + b;

    private static double FbClearBottom(FbBfc bfc, string clear)
    {
        var b = 0.0;
        foreach (var f in bfc.Floats)
            if (clear == "both" || (clear == "left" && f.left) || (clear == "right" && !f.left)) b = Math.Max(b, f.y1);
        return b;
    }

    /// <summary>An in-flow block box at the containing block's content box, its border box top at
    /// the given top: the content width fills the container unless declared; auto side margins
    /// centre a declared width; the height is the content's unless declared (content overflows a
    /// declared height).</summary>
    private static FbBox FbLayoutBlock(FbState fb, HtmlNode el, FbStyle st, FbBoxProps p, double cbX, double cbW, double top, FbBfc bfc, bool isBfc, double collapsedTop = 0)
    {
        var box = new FbBox { Node = el, St = st, P = p, IsBfc = isBfc };
        double contentW;
        if (p.WidthAuto)
        {
            contentW = Math.Max(0, cbW - p.Margin.H - p.ChromeH);
            box.X = cbX + p.Margin.L;
        }
        else
        {
            contentW = p.WidthPt;
            var outer = contentW + p.ChromeH;
            var free = cbW - outer - (p.MarginLeftAuto ? 0 : p.Margin.L) - (p.MarginRightAuto ? 0 : p.Margin.R);
            if (p.MarginLeftAuto && p.MarginRightAuto) box.X = cbX + Math.Max(0, free / 2);
            else if (p.MarginLeftAuto) box.X = cbX + Math.Max(0, free);
            else box.X = cbX + p.Margin.L;
        }
        box.W = contentW + p.ChromeH;
        box.Y = top;
        var inner = isBfc ? new FbBfc() : bfc;
        var cursor = box.ContentY;
        FbLayoutChildren(fb, box, el, st, box.ContentX, contentW, ref cursor, inner, isBfc ? 0 : collapsedTop);
        var contentH = cursor - box.ContentY;
        if (isBfc) contentH = Math.Max(contentH, inner.Bottom() - box.ContentY);
        box.H = p.ChromeV + (p.HeightAuto ? contentH : p.HeightPt);
        return box;
    }

    /// <summary>Place a floated element on the current line: its margin box takes the first line at
    /// or below the cursor with room beside the earlier floats (below all of them when nothing
    /// frees enough, or when it is wider than the container), anchored to its side, its
    /// margin-top added; a widthless float shrinks to its content, capped at the free width.</summary>
    private static void FbPlaceFloat(FbState fb, FbBox parent, HtmlNode el, FbStyle st, FbBoxProps p, double cbX, double cbW, double lineTop, FbBfc bfc)
    {
        var left = p.Float == "left";
        double contentW;
        if (p.WidthAuto)
        {
            var avail = Math.Max(0, cbW - p.Margin.H - p.ChromeH);
            contentW = Math.Min(FbShrinkToFit(fb, el, st, p, avail), avail);
        }
        else contentW = p.WidthPt;
        var outerW = contentW + p.ChromeH + p.Margin.H;
        var y = lineTop;
        while (true)
        {
            var (s0, s1) = bfc.Segment(y, cbX, cbW);
            if (s1 - s0 + FbEpsilon >= outerW) break;
            var next = bfc.NextBottom(y);
            if (next == double.MaxValue) break;
            y = next;
        }
        var (x0, x1) = bfc.Segment(y, cbX, cbW);
        if (x1 - x0 + FbEpsilon < outerW) { x0 = cbX; x1 = cbX + cbW; }
        var box = new FbBox { Node = el, St = st, P = p, IsBfc = true, IsFloat = true };
        box.W = contentW + p.ChromeH;
        box.X = left ? x0 + p.Margin.L : x1 - p.Margin.R - box.W;
        box.Y = y + p.Margin.T;
        if (el.Tag == "table") { box.IsTable = true; FbLayoutTableInto(fb, box, cbW); }
        else FbLayoutBoxContent(fb, box, el, st, contentW);
        parent.Kids.Add(box);
        // (probed: an empty caption float, zero tall, moves nothing beside it)
        if (box.H > FbEpsilon) bfc.Floats.Add((box.X - p.Margin.L, box.X + box.W + p.Margin.R, y, box.Y + box.H + p.Margin.B, left));
    }

    /// <summary>Lay a formatting-context box's content out (an inline element's content as one
    /// run of its own font, an element's children as blocks and lines) and size its height.</summary>
    private static void FbLayoutBoxContent(FbState fb, FbBox box, HtmlNode el, FbStyle st, double contentW)
    {
        var inner = new FbBfc();
        var cursor = box.ContentY;
        FbLayoutChildren(fb, box, el, st, box.ContentX, contentW, ref cursor, inner);
        var contentH = Math.Max(cursor, inner.Bottom()) - box.ContentY;
        box.H = box.P.ChromeV + (box.P.HeightAuto ? contentH : box.P.HeightPt);
    }

    private static bool FbIsInlineTag(string tag) => tag is "span" or "b" or "strong" or "i" or "em" or "a" or "font" or "label" or "u" or "small" or "big" or "sup" or "sub" or "code";

    /// <summary>The shrink-to-fit width of a widthless box: the larger of its floats' row (their
    /// margin boxes side by side), its widest line's content and its sized children.</summary>
    private static double FbShrinkToFit(FbState fb, HtmlNode el, FbStyle st, FbBoxProps p, double avail)
    {
        var wide = Math.Max(avail, FbShrinkTrialWidth);
        var trial = new FbBox { Node = el, St = st, P = p, IsBfc = true, X = 0, Y = 0, W = wide + p.ChromeH };
        var inner = new FbBfc();
        var cursor = trial.ContentY;
        FbLayoutChildren(fb, trial, el, st, trial.ContentX, wide, ref cursor, inner);
        var floatsRow = 0.0;
        foreach (var f in inner.Floats) floatsRow += f.x1 - f.x0;
        return Math.Max(floatsRow, FbIntrinsicWidth(trial));
    }

    private const double FbShrinkTrialWidth = 1e5;              // a trial container wide enough to hold any one line

    /// <summary>The content width a box needs when nothing wraps, from its content left: its
    /// lines' advance (a line beside a float counts its own content only), its sized, inline and
    /// table children's margin boxes, its auto-width children's own content.</summary>
    private static double FbIntrinsicWidth(FbBox box)
    {
        var width = 0.0;
        foreach (var line in box.Lines) width = Math.Max(width, line.Advance);
        foreach (var k in box.Kids)
        {
            if (k.IsFloat || k.IsInline) continue;                // (an inline box is in its line's advance)
            if (k.IsTable || !k.P.WidthAuto) width = Math.Max(width, k.X + k.W + k.P.Margin.R - box.ContentX);
            else width = Math.Max(width, k.ContentX - box.ContentX + FbIntrinsicWidth(k));
        }
        return width;
    }

    /// <summary>An inline-block child as one item of the run: a box laid out in its own formatting
    /// context at the origin (seated on its line at flush), its baseline the last line's, else its
    /// margin box bottom; a widthless one shrinks to fit.</summary>
    private static FbItem FbInlineBoxItem(FbState fb, FbBox parent, HtmlNode el, FbStyle st, FbBoxProps p, double cbW)
    {
        if (p.WidthAuto)
        {
            var avail = Math.Max(0, cbW - p.Margin.H - p.ChromeH);
            p.WidthAuto = false;
            p.WidthPt = el.Tag == "table" ? 0 : Math.Min(FbShrinkToFit(fb, el, st, p, avail), avail);
            if (el.Tag == "table") p.WidthAuto = true;
        }
        var box = el.Tag == "table" ? FbLayoutTable(fb, el, st, p, 0, cbW, 0) : FbLayoutBlock(fb, el, st, p, 0, cbW, 0, new FbBfc(), true);
        box.X = p.Margin.L;
        box.IsInline = true;
        parent.Kids.Add(box);
        var baseline = FbLastBaseline(box);
        var above = baseline is { } bl ? bl - box.Y + p.Margin.T : box.OuterH;
        // (probed: an inline-block box stands with its top on the line top - a textarea's line is its
        //  box, a value box's is its own height - whatever its baseline)
        return new FbItem { Box = box, Adv = box.OuterW, Above = above, Below = box.OuterH - above, St = st, VAlignTop = true };
    }

    /// <summary>The baseline (continuous y) of a box's last line, through its last in-flow child; null without one.</summary>
    private static double? FbLastBaseline(FbBox box)
    {
        double? last = null;
        if (box.Lines.Count > 0) last = box.Lines[^1].Y + box.Lines[^1].Above;
        for (var i = box.Kids.Count - 1; i >= 0; i--)
        {
            var k = box.Kids[i];
            if (k.IsFloat || k.IsInline) continue;
            if (FbLastBaseline(k) is { } kb && (last is null || kb > last)) last = kb;
            break;
        }
        return last;
    }
}
