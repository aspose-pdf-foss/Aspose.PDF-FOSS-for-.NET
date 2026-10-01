using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One inline item of a run: a word, a space, an inline margin spacer, a forced break,
    /// or a box (an inline-block or a control).</summary>
    private sealed class FbItem
    {
        public string Text = "";
        public FbStyle St = null!;
        public double Adv, Above, Below;
        public double X;                                        // pen offset in its line, set at flush
        public bool IsSpace, IsBreak, IsSpacer, VAlignTop;
        public FbBox? Box;
        public bool IsContent => !IsSpace && !IsBreak && !IsSpacer;
        public static FbItem Break() => new() { IsBreak = true, St = new FbStyle() };
        public static FbItem Spacer(double adv, FbStyle st) => new() { IsSpacer = true, Adv = adv, St = st };
    }

    private sealed class FbLine
    {
        public double X, Y, W, Above, Below;                    // X = segment left, W = segment width
        public List<FbItem> Items = new();
        public double Height => Above + Below;
        public double Advance;                                  // the items' total advance
    }

    /// <summary>The line box of a size in a face in px: the declared line height, else the hhea
    /// line rounded to whole px.</summary>
    private static double FbLineBoxPx(FbState fb, FbStyle st)
    {
        if (st.LineHeightPx > 0) return st.LineHeightPx;
        if (st.LineHeightFactor > 0) return st.LineHeightFactor * st.Px;
        var m = FbStyleMetrics(fb, st);
        return Math.Round(st.Px * m.line, MidpointRounding.AwayFromZero);
    }

    /// <summary>The hhea metrics of a style's face: one of the sheet's own, else the built-in measuring face.</summary>
    private static (double asc, double desc, double line) FbStyleMetrics(FbState fb, FbStyle st)
        => st.Own is { } own ? FbOwnMetrics(own) : FbFaceMetrics(fb, st.MeasureFace);

    /// <summary>The baseline's distance below the line box top, in pt: half the leading over the
    /// ascent + descent, plus the ascent.</summary>
    private static double FbAbovePt(FbState fb, FbStyle st)
    {
        var m = FbStyleMetrics(fb, st);
        var l = FbLineBoxPx(fb, st);
        return ((l - (m.asc + m.desc) * st.Px) / 2 + m.asc * st.Px) * FbPxPt;
    }

    private static double FbBelowPt(FbState fb, FbStyle st) => FbLineBoxPx(fb, st) * FbPxPt - FbAbovePt(fb, st);

    private static double FbMeasure(FbStyle st, string text) => text.Length == 0 ? 0
        : st.Own is { } own ? FbMeasureOwn(own, text, st.Pt) : MeasureFaceText(st.MeasureFace, text, st.Pt);

    /// <summary>Add a text node to the run as words and spaces (entities decoded, whitespace
    /// collapsed; a no-break space stays inside its word).</summary>
    private static void FbAddText(FbState fb, List<FbItem> run, string raw, FbStyle st)
    {
        var text = Regex.Replace(DecodeEntities(raw), "[ \\t\\r\\n]+", " ");
        if (text.Length == 0) return;
        if (st.Uppercase) text = text.ToUpperInvariant();
        var above = FbAbovePt(fb, st);
        var below = FbBelowPt(fb, st);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == ' ')
            {
                run.Add(new FbItem { Text = " ", St = st, IsSpace = true, Adv = FbMeasure(st, " "), Above = above, Below = below });
                i++;
                continue;
            }
            var j = i;
            while (j < text.Length && text[j] != ' ') j++;
            var word = text[i..j];
            run.Add(new FbItem { Text = word, St = st, Adv = FbMeasure(st, word), Above = above, Below = below });
            i = j;
        }
    }

    private static bool FbRunHasContent(List<FbItem> run)
    {
        foreach (var it in run) if (it.IsContent) return true;
        return false;
    }

    /// <summary>Flush the run as lines at the cursor: each line takes the free segment beside the
    /// floats crossing its top, wraps greedily on the words (never inside a no-break run), stands on
    /// the block's strut with its items' own extents, aligns per the block's text-align, and a line
    /// that would straddle the page band moves to the next sheet.</summary>
    private static void FbFlushRun(FbState fb, FbBox box, List<FbItem> run, double cx, double cw, ref double cursor, FbBfc bfc)
    {
        if (run.Count == 0) return;
        var items = new List<FbItem>(run);
        run.Clear();
        if (!FbRunHasContent(items)) return;
        var strutAbove = FbAbovePt(fb, box.St);
        var strutBelow = FbBelowPt(fb, box.St);
        var pos = 0;
        while (pos < items.Count)
        {
            while (pos < items.Count && items[pos].IsSpace) pos++;
            if (pos >= items.Count) break;
            if (items[pos].IsBreak) { pos++; cursor += strutAbove + strutBelow; continue; }
            var (s0, s1) = bfc.Segment(cursor, cx, cw);
            var lineW = s1 - s0;
            var end = FbLineEnd(items, pos, lineW, box.St.Nowrap);
            if (end == pos) end = pos + 1;
            if (FbItemsAdvance(items, pos, end) > lineW + FbEpsilon && s1 - s0 < cw - FbEpsilon)
            {
                // the line does not fit beside the floats (a no-wrap line included): it drops
                // below the next float bottom; only a line the container itself cannot hold overflows
                var next = bfc.NextBottom(cursor);
                if (next != double.MaxValue) { cursor = next; continue; }
            }
            // (quirks: a line holding only replaced boxes has no strut - the block is exactly the box)
            var strutless = fb.quirks && !FbLineHasText(items, pos, end);
            var line = FbBuildLine(items, pos, end, s0, lineW, strutless ? 0 : strutAbove, strutless ? 0 : strutBelow, box.St.Align);
            var pageBottom = FbPageBottom(fb, FbPageOf(fb, cursor).page);
            if (cursor < pageBottom - FbEpsilon && cursor + line.Height > pageBottom + FbEpsilon && line.Height <= fb.laterPageStep)
            {
                cursor = pageBottom;
                continue;
            }
            line.Y = cursor;
            FbSeatBoxes(line);
            box.Lines.Add(line);
            cursor += line.Height;
            pos = end;
            while (pos < items.Count && items[pos].IsBreak) { pos++; if (pos >= items.Count || items[pos].IsBreak) cursor += strutAbove + strutBelow; }
        }
    }

    /// <summary>Does the line's span of items carry any text (a word, not a box)?</summary>
    private static bool FbLineHasText(List<FbItem> items, int pos, int end)
    {
        for (var i = pos; i < end && i < items.Count; i++)
            if (items[i].IsContent && items[i].Box is null) return true;
        return false;
    }

    private static double FbItemsAdvance(List<FbItem> items, int pos, int end)
    {
        var x = 0.0;
        var last = end - 1;
        while (last > pos && items[last].IsSpace) last--;
        for (var i = pos; i <= last; i++) x += items[i].Adv;
        return x;
    }

    /// <summary>The index after the last item of a line starting at pos in a width: words and the
    /// spaces between them, stopping before the first word that would overflow (a break ends the line).</summary>
    private static int FbLineEnd(List<FbItem> items, int pos, double lineW, bool nowrap)
    {
        var x = 0.0;
        var end = pos;
        for (var i = pos; i < items.Count; i++)
        {
            var it = items[i];
            if (it.IsBreak) break;
            if (it.IsSpace) { x += it.Adv; continue; }
            if (!nowrap && end > pos && x + it.Adv > lineW + FbEpsilon) break;
            x += it.Adv;
            end = i + 1;
        }
        return end;
    }

    /// <summary>Build a line from the items: the pens, the strut and the items' extents (a
    /// top-aligned box only deepens the line), the alignment shift.</summary>
    private static FbLine FbBuildLine(List<FbItem> items, int pos, int end, double x0, double lineW, double strutAbove, double strutBelow, string align)
    {
        var line = new FbLine { X = x0, W = lineW, Above = strutAbove, Below = strutBelow };
        var last = end - 1;
        while (last > pos && items[last].IsSpace) last--;
        var x = 0.0;
        var topHeight = 0.0;
        for (var i = pos; i <= last; i++)
        {
            var it = items[i];
            it.X = x;
            x += it.Adv;
            line.Items.Add(it);
            if (it.VAlignTop) { topHeight = Math.Max(topHeight, it.Above + it.Below); continue; }
            line.Above = Math.Max(line.Above, it.Above);
            line.Below = Math.Max(line.Below, it.Below);
        }
        if (topHeight > line.Height) line.Below = topHeight - line.Above;
        line.Advance = x;
        var slack = lineW - x;
        var shift = align switch { "center" => slack / 2, "right" => slack, _ => 0 };
        if (shift != 0) foreach (var it in line.Items) it.X += shift;
        return line;
    }

    /// <summary>Seat the run's boxes (inline-blocks, controls) on the built line: a top-aligned box's
    /// margin top on the line top, any other's baseline on the line's, moved from the origin they
    /// were laid out at.</summary>
    private static void FbSeatBoxes(FbLine line)
    {
        foreach (var it in line.Items)
        {
            if (it.Box is not { } b) continue;
            var dx = line.X + it.X + b.P.Margin.L - b.X;
            var dy = (it.VAlignTop ? line.Y : line.Y + line.Above - it.Above) + b.P.Margin.T - b.Y;
            FbShift(b, dx, dy);
        }
    }

    private static void FbShift(FbBox box, double dx, double dy)
    {
        box.X += dx; box.Y += dy;
        foreach (var l in box.Lines) { l.X += dx; l.Y += dy; }
        foreach (var k in box.Kids) FbShift(k, dx, dy);
        if (box.Grid is { } g) g.Shift(dx, dy);
    }
}
