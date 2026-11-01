using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

// The rows of the column claim: a flex container's items and a run of table-cell boxes are seated
// side by side, each a formatting context of its own column width, the row as tall as its tallest.
internal static partial class HtmlToPdfConverter
{
    private const double FbMarkerGapEm = 0.4;                 // a list marker's distance from its item's content edge
    private const string FbInlineSvgScheme = "inline-svg:";   // the source of an inline drawing's placeholder picture
    private const int FbSvgRootScanBytes = 2048;              // the root tag of a drawing is read from its head
    private const string FbBulletMarker = "•";

    /// <summary>One box of a row: its element, its styles and the content width the row gives it.</summary>
    private sealed class FbRowItem
    {
        public HtmlNode Node = null!;
        public FbStyle St = null!;
        public FbBoxProps P = null!;
#pragma warning disable S3218 // data-only nested type: its members are always reached through an instance
        public double ContentW;                               // the base size, then the size the row settles
        public double Grow, Shrink = 1;
        public bool WidthDeclared;
        public double OuterW => ContentW + P.ChromeH + P.Margin.H;
#pragma warning restore S3218
    }

    private static bool FbIsFlexRow(FbState fb, HtmlNode el, FbBoxProps p) => p.Display is "flex" or "inline-flex" && el.Tag.Length > 0;

    /// <summary>A flex container's children as row items: every element child is one (a text child
    /// is an anonymous one), its base size the declared width or flex basis, else its content's
    /// widest line.</summary>
    private static List<FbRowItem> FbFlexItems(FbState fb, FbBox box, HtmlNode el, FbStyle st, double cw)
    {
        var items = new List<FbRowItem>();
        foreach (var c in new List<HtmlNode>(el.Children))
        {
            var node = c;
            if (c.Tag.Length == 0)
            {
                if (c.Text.Trim().Length == 0) continue;
                node = new HtmlNode { Tag = "::item", Parent = el };
                node.Children.Add(new HtmlNode { Text = c.Text, Parent = node });
            }
            else if (c.Tag is "script" or "style" or "head" or "title" or "meta" or "link" or "br" or "noscript") continue;
            var cst = FbStyleOf(fb, node, st);
            var p = FbPropsOf(fb, node, cw, box.P.HeightAuto ? 0 : box.P.HeightPt, cst.Px);
            if (p.Display == "none" || p.OutOfFlow) continue;
            var item = new FbRowItem { Node = node, St = cst, P = p, WidthDeclared = !p.WidthAuto };
            FbReadFlexFactors(fb, node, item, cw, cst.Px);
            if (!item.WidthDeclared) item.ContentW = FbMaxContent(fb, node, cst, p);
            items.Add(item);
        }
        return items;
    }

    /// <summary>The widest line of a box's content (a picture: its own width).</summary>
    private static double FbMaxContent(FbState fb, HtmlNode node, FbStyle st, FbBoxProps p)
    {
        if (node.Tag == "img") return FbLoadPicture(fb, node, p, st.Px) is null ? 0 : p.WidthPt;
        if (node.Tag == "table") return FbLayoutTable(fb, node, st, p, 0, FbShrinkTrialWidth, 0).W;
        var w = FbShrinkToFit(fb, node, st, p, 0);
        return p.MaxWidthPt > 0 ? Math.Min(w, p.MaxWidthPt) : w;
    }

    /// <summary>An item's grow and shrink factors and its basis: the `flex` shorthand (a lone number
    /// is the grow factor on a zero basis), then the longhands; a declared basis is the base size.</summary>
    private static void FbReadFlexFactors(FbState fb, HtmlNode node, FbRowItem item, double cw, double emPx)
    {
        item.ContentW = item.P.WidthAuto ? 0 : item.P.WidthPt;
        string? basis = null;
        var flex = (FbDecl(fb, node, "flex") ?? "").Trim().ToLowerInvariant();
        if (flex == "none") item.Shrink = 0;
        else if (flex == "auto") item.Grow = 1;
        else if (flex.Length > 0)
        {
            var parts = flex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (FbNumber(parts[0]) is { } g) { item.Grow = g; basis = parts.Length == 1 ? "0" : null; }
            if (parts.Length > 1 && FbNumber(parts[1]) is { } s) item.Shrink = s;
            if (parts.Length > 1 && FbNumber(parts[^1]) is null) basis = parts[^1];
        }
        if (FbNumber(FbDecl(fb, node, "flex-grow")) is { } grow) item.Grow = grow;
        if (FbNumber(FbDecl(fb, node, "flex-shrink")) is { } shrink) item.Shrink = shrink;
        basis = FbDecl(fb, node, "flex-basis") ?? basis;
        if (!string.IsNullOrEmpty(basis) && FbSizePt(basis, cw, emPx) is { } basisPt)
        {
            item.ContentW = Math.Max(0, basisPt);
            item.WidthDeclared = true;
        }
    }

    private static double? FbNumber(string? value) =>
        double.TryParse((value ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>A gap of the container in pt: the longhand, else the `gap` shorthand's part (row first, column second).</summary>
    private static double FbGapPt(FbState fb, HtmlNode el, bool column, double emPx)
    {
        var v = FbDecl(fb, el, column ? "column-gap" : "row-gap");
        if (string.IsNullOrEmpty(v))
        {
            var parts = (FbDecl(fb, el, "gap") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return 0;
            v = column && parts.Length > 1 ? parts[1] : parts[0];
        }
        return Math.Max(0, FbSizePt(v, 0, emPx) ?? 0);
    }

    /// <summary>Lay a flex container's items out: a row container breaks them into lines when it
    /// wraps, shares a line's free width by the grow factors (or takes an overflow off by the shrink
    /// factors), justifies what is left and stretches the items to the line; a column container
    /// stacks them at the container's width, its gap between them.</summary>
    private static void FbLayoutFlexRow(FbState fb, FbBox box, HtmlNode el, FbStyle st, double cx, double cw, ref double cursor)
    {
        var items = FbFlexItems(fb, box, el, st, cw);
        var trial = FbIsTrialWidth(cw);                         // a shrink-to-fit measure: nothing grows or justifies
        var align = (FbDecl(fb, el, "align-items") ?? "stretch").Trim().ToLowerInvariant();
        if (FbFlexColumn(fb, el))
        {
            var rowGap = FbGapPt(fb, el, column: false, st.Px);
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (!it.P.WidthAuto) it.ContentW = it.P.WidthPt;
                else if (align == "stretch" || align == "normal") it.ContentW = Math.Max(0, cw - it.P.ChromeH - it.P.Margin.H);
                else it.ContentW = Math.Min(FbMaxContent(fb, it.Node, it.St, it.P), Math.Max(0, cw - it.P.ChromeH - it.P.Margin.H));
                cursor += FbPlaceRow(fb, box, new List<FbRowItem> { it }, cx, cursor, 0, "start") + (i < items.Count - 1 ? rowGap : 0);
            }
            return;
        }
        var gap = FbGapPt(fb, el, column: true, st.Px);
        var wrap = (FbDecl(fb, el, "flex-wrap") ?? FbDecl(fb, el, "flex-flow") ?? "").IndexOf("wrap", StringComparison.OrdinalIgnoreCase) >= 0
            && (FbDecl(fb, el, "flex-wrap") ?? "").IndexOf("nowrap", StringComparison.OrdinalIgnoreCase) < 0;
        var justify = (FbDecl(fb, el, "justify-content") ?? "flex-start").Trim().ToLowerInvariant();
        var pos = 0;
        while (pos < items.Count)
        {
            var end = pos + 1;
            var used = items[pos].OuterW;
            while (end < items.Count && (!wrap || trial || used + gap + items[end].OuterW <= cw + FbEpsilon))
            {
                used += gap + items[end].OuterW;
                end++;
            }
            var line = items.GetRange(pos, end - pos);
            var free = trial ? 0 : FbSettleFlexLine(line, cw - used);
            var (lead, between) = FbJustify(justify, free, line.Count);
            cursor += FbPlaceRow(fb, box, line, cx + lead, cursor, gap + between, align);
            if (end < items.Count) cursor += FbGapPt(fb, el, column: false, st.Px);
            pos = end;
        }
    }

    /// <summary>Settle a line's item widths against its free width: a positive remainder goes to the
    /// items by their grow factors, a negative one comes off them by their shrink factors weighted
    /// with their base sizes; returns the width still free.</summary>
    private static double FbSettleFlexLine(List<FbRowItem> line, double free)
    {
        if (free > FbEpsilon)
        {
            var grow = 0.0;
            foreach (var it in line) grow += it.Grow;
            if (grow <= 0) return free;
            foreach (var it in line) it.ContentW += free * it.Grow / Math.Max(grow, 1);
            return grow >= 1 ? 0 : free * (1 - grow);
        }
        if (free < -FbEpsilon)
        {
            var weight = 0.0;
            foreach (var it in line) weight += it.Shrink * it.ContentW;
            if (weight <= 0) return 0;
            foreach (var it in line) it.ContentW = Math.Max(0, it.ContentW + free * it.Shrink * it.ContentW / weight);
        }
        return 0;
    }

    /// <summary>The offset before the first item and the extra space between items for a justification.</summary>
    private static (double lead, double between) FbJustify(string justify, double free, int count)
    {
        if (free <= FbEpsilon) return (0, 0);
        return justify switch
        {
            "center" => (free / 2, 0),
            "flex-end" or "end" or "right" => (free, 0),
            "space-between" when count > 1 => (0, free / (count - 1)),
            "space-around" => (free / count / 2, free / count),
            "space-evenly" => (free / (count + 1), free / (count + 1)),
            _ => (0, 0),
        };
    }

    /// <summary>Seat a row's items from a left edge at a top, each a formatting context of its
    /// settled width; the row is as tall as its tallest margin box, an item of automatic height
    /// stretching to it unless the alignment says otherwise. Returns the row's height.</summary>
    private static double FbPlaceRow(FbState fb, FbBox parent, List<FbRowItem> items, double x0, double top, double gap, string align)
    {
        var boxes = new List<FbBox>();
        var x = x0;
        var height = 0.0;
        foreach (var it in items)
        {
            var p = it.P;
            p.WidthAuto = false;
            p.WidthPt = it.ContentW;
            p.MaxWidthPt = 0;
            var outer = it.OuterW;
            var box = it.Node.Tag == "table"
                ? FbLayoutTable(fb, it.Node, it.St, p, x, outer, top + p.Margin.T)
                : FbLayoutBlock(fb, it.Node, it.St, p, x, outer, top + p.Margin.T, new FbBfc(), true);
            box.X = x + p.Margin.L;
            if (it.Node.Tag == "img") box.Picture = FbLoadPicture(fb, it.Node, p, it.St.Px);
            parent.Kids.Add(box);
            boxes.Add(box);
            height = Math.Max(height, box.OuterH);
            x += outer + gap;
        }
        foreach (var box in boxes)
        {
            var slack = height - box.OuterH;
            if (slack <= FbEpsilon) continue;
            if (align is "stretch" or "normal") { if (box.P.HeightAuto && !box.IsTable) box.H += slack; }
            else if (align == "center") FbShift(box, 0, slack / 2);
            else if (align is "flex-end" or "end") FbShift(box, 0, slack);
        }
        return height;
    }

    /// <summary>A run of sibling table-cell boxes (no table's cells) as one row from the first of
    /// them: a declared width holds, the others take their content's width and, in a table box of
    /// a declared width, share what is left in proportion to it; an overfull row gives the excess
    /// back the same way.</summary>
    private static void FbLayoutCellRow(FbState fb, FbBox parent, HtmlNode first, FbStyle st, double cx, double cw, ref double cursor)
    {
        var cells = new List<FbRowItem>();
        var siblings = first.Parent!.Children;
        for (var i = siblings.IndexOf(first); i < siblings.Count; i++)
        {
            var c = siblings[i];
            if (c.Tag.Length == 0) { if (c.Text.Trim().Length == 0) { continue; } break; }
            var cst = FbStyleOf(fb, c, st);
            var p = FbPropsOf(fb, c, cw, 0, cst.Px);
            if (p.Display != "table-cell") break;
            var cell = new FbRowItem { Node = c, St = cst, P = p, WidthDeclared = !p.WidthAuto };
            cell.ContentW = cell.WidthDeclared ? p.WidthPt : FbMaxContent(fb, c, cst, p);
            cells.Add(cell);
        }
        var used = 0.0; var auto = 0.0;
        foreach (var cell in cells) { used += cell.OuterW; if (!cell.WidthDeclared) auto += cell.ContentW; }
        var free = cw - used;
        var fills = parent.P.Display == "table" && !parent.P.WidthAuto;
        if (!FbIsTrialWidth(cw) && auto > 0 && (free < -FbEpsilon || (fills && free > FbEpsilon)))
            foreach (var cell in cells)
                if (!cell.WidthDeclared) cell.ContentW = Math.Max(0, cell.ContentW + free * cell.ContentW / auto);
        cursor += FbPlaceRow(fb, parent, cells, cx, cursor, 0, "stretch");
    }

    /// <summary>The children only the column claim lays out: an absolutely positioned box, a run
    /// of table-cell boxes, a picture that loads. True when the child is done.</summary>
    private static bool FbColumnChild(FbState fb, FbBox parent, HtmlNode c, FbStyle cst, FbBoxProps p, double cx, double cw, ref double cursor, FbBfc bfc, FbFlow flow)
    {
        if (p.OutOfFlow)
        {
            FbPlaceOutOfFlow(fb, parent, c, cst, p, cx, cw, cursor + flow.PendingMb);
            return true;
        }
        if (p.Display == "table-cell" && c.Tag is not ("td" or "th"))
        {
            // (a cell after another cell stands in the row its first cell laid out)
            if (FbFollowsCell(fb, c)) return true;
            FbFlushFlow(fb, parent, flow, cx, cw, ref cursor, bfc);
            cursor += flow.PendingMb;
            flow.PendingMb = 0;
            FbLayoutCellRow(fb, parent, c, parent.St, cx, cw, ref cursor);
            return true;
        }
        return c.Tag == "img" && FbAddLoadedPicture(fb, parent, c, cst, p, cx, cw, ref cursor, bfc, flow);
    }

    /// <summary>An absolutely positioned box: out of the flow, at its left / right / top offsets
    /// from the padding box of the nearest positioned ancestor (the sheet's content box without
    /// one), else where it would have stood; a widthless one shrinks to its content.</summary>
    private static void FbPlaceOutOfFlow(FbState fb, FbBox parent, HtmlNode el, FbStyle st, FbBoxProps p, double cx, double cw, double staticTop)
    {
        double cbX = fb.marginLeft, cbY = fb.marginTop, cbW = fb.sheetW - fb.marginLeft - fb.marginRight;
        if (fb.positioned.Count > 0)
        {
            var cb = fb.positioned.Peek();
            cbX = cb.X + cb.P.Border.L; cbY = cb.Y + cb.P.Border.T; cbW = cb.W - cb.P.Border.H;
        }
        var left = FbOffsetPt(fb, el, "left", cbW, st.Px);
        var right = FbOffsetPt(fb, el, "right", cbW, st.Px);
        var top = FbOffsetPt(fb, el, "top", 0, st.Px);
        double contentW;
        if (!p.WidthAuto) contentW = p.WidthPt;
        else if (left is not null && right is not null) contentW = Math.Max(0, cbW - left.Value - right.Value - p.Margin.H - p.ChromeH);
        else contentW = Math.Min(FbMaxContent(fb, el, st, p), Math.Max(0, cbW - p.Margin.H - p.ChromeH));
        var box = new FbBox { Node = el, St = st, P = p, IsBfc = true, IsFloat = true };
        box.W = contentW + p.ChromeH;
        if (left is not null) box.X = cbX + left.Value + p.Margin.L;
        else if (right is not null) box.X = cbX + cbW - right.Value - p.Margin.R - box.W;
        else box.X = cx + p.Margin.L;
        box.Y = (top is not null ? cbY + top.Value : staticTop) + p.Margin.T;
        fb.positioned.Push(box);
        if (el.Tag == "img" && FbLoadPicture(fb, el, p, st.Px) is { } bytes) { box.Picture = bytes; box.W = p.WidthPt + p.ChromeH; box.H = p.HeightPt + p.ChromeV; }
        else FbLayoutBoxContent(fb, box, el, st, contentW);
        fb.positioned.Pop();
        parent.Kids.Add(box);
    }

    /// <summary>Is the element's previous sibling box (blank text aside) a table-cell box too?</summary>
    private static bool FbFollowsCell(FbState fb, HtmlNode el)
    {
        var siblings = el.Parent!.Children;
        for (var i = siblings.IndexOf(el) - 1; i >= 0; i--)
        {
            var s = siblings[i];
            if (s.Tag.Length == 0) { if (s.Text.Trim().Length == 0) { continue; } return false; }
            return FbDisplayOf(fb, s) == "table-cell";
        }
        return false;
    }

    /// <summary>A width so large that it can only be a shrink-to-fit trial's.</summary>
    private static bool FbIsTrialWidth(double cw) => cw >= FbShrinkTrialWidth / 2;

    private static double? FbOffsetPt(FbState fb, HtmlNode el, string side, double pctBase, double emPx)
    {
        var v = FbDecl(fb, el, side);
        return string.IsNullOrEmpty(v) ? null : FbSizePt(v, pctBase, emPx, allowNegative: true);
    }

    /// <summary>A picture's bytes when it loads, its box properties sized: the declared width and
    /// height, one of them with the picture's own proportions, else its pixel size; a max-width caps it.</summary>
    private static byte[]? FbLoadPicture(FbState fb, HtmlNode el, FbBoxProps p, double emPx)
    {
        if (el.Attrs is null || !el.Attrs.TryGetValue("src", out var src)) return null;
        if (!fb.pictureBytes.TryGetValue(src, out var bytes))
        {
            try { bytes = FbInlineDrawing(fb, src) ?? LoadConverterImage(src, fb.options); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { bytes = null; }
            fb.pictureBytes[src] = bytes;
        }
        if (bytes is null || TryReadImagePixelSize(bytes) is not (var pw, var ph) || pw <= 0 || ph <= 0) return null;
        // (a drawing's own stated size stands over the bare numerals its placeholder carries)
        var drawn = FbInlineDrawingSizePx(fb, src, emPx);
        var (attrW, attrH) = drawn ?? FbPictureSizePx(fb, el);
        double w = !p.WidthAuto && drawn is null ? p.WidthPt : attrW * FbPxPt, h = !p.HeightAuto && drawn is null ? p.HeightPt : attrH * FbPxPt;
        if (w <= 0 && h <= 0) { w = pw * FbPxPt; h = ph * FbPxPt; }
        else if (w <= 0) w = h * pw / ph;
        else if (h <= 0) h = w * ph / pw;
        if (p.MaxWidthPt > 0 && w > p.MaxWidthPt) { h *= p.MaxWidthPt / w; w = p.MaxWidthPt; }
        p.WidthAuto = false; p.WidthPt = w;
        p.HeightAuto = false; p.HeightPt = h;
        return bytes;
    }

    /// <summary>The raster of an inline drawing the markup holds a placeholder picture for; null for any other source.</summary>
    private static byte[]? FbInlineDrawing(FbState fb, string src)
    {
        if (!src.StartsWith(FbInlineSvgScheme, StringComparison.Ordinal) || fb.inlineSvgs is not { } svgs) return null;
        return int.TryParse(src[FbInlineSvgScheme.Length..], out var index) && index >= 0 && index < svgs.Count
            ? ImageRasterizer.RasterizeSvg(svgs[index]) : null;
    }

    private static readonly System.Text.RegularExpressions.Regex FbSvgRootSizeRx = new(@"^<svg\b[^>]*?\swidth\s*=\s*[""'](?<w>[\d.]+)(?<wu>em|px)?[""'][^>]*?\sheight\s*=\s*[""'](?<h>[\d.]+)(?<hu>em|px)?[""']",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The size an inline drawing's root states, in px: a length in em is of the running
    /// font size (an icon drawn one em square stands as tall as its line's letters); null for a
    /// picture that is no inline drawing or a root that states no such size.</summary>
    private static (double wPx, double hPx)? FbInlineDrawingSizePx(FbState fb, string src, double emPx)
    {
        if (!src.StartsWith(FbInlineSvgScheme, StringComparison.Ordinal) || fb.inlineSvgs is not { } svgs) return null;
        if (!int.TryParse(src[FbInlineSvgScheme.Length..], out var index) || index < 0 || index >= svgs.Count) return null;
        var head = System.Text.Encoding.UTF8.GetString(svgs[index], 0, Math.Min(svgs[index].Length, FbSvgRootScanBytes));
        var m = SafeRegex.Match(FbSvgRootSizeRx, head.TrimStart());
        if (!m.Success) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = double.Parse(m.Groups["w"].Value, inv) * (m.Groups["wu"].Value.Equals("em", StringComparison.OrdinalIgnoreCase) ? emPx : 1);
        var h = double.Parse(m.Groups["h"].Value, inv) * (m.Groups["hu"].Value.Equals("em", StringComparison.OrdinalIgnoreCase) ? emPx : 1);
        return (w, h);
    }

    /// <summary>A picture that loads is a replaced box of its size: floated, a block, or seated on
    /// its line with its bottom on the baseline. False when it does not load.</summary>
    private static bool FbAddLoadedPicture(FbState fb, FbBox parent, HtmlNode el, FbStyle st, FbBoxProps p, double cx, double cw, ref double cursor, FbBfc bfc, FbFlow flow)
    {
        if (FbLoadPicture(fb, el, p, st.Px) is not { } bytes) return false;
        if (p.Float != "none")
        {
            cursor += flow.PendingMb;
            flow.PendingMb = 0;
            FbPlaceFloat(fb, parent, el, st, p, cx, cw, cursor, bfc);
            parent.Kids[^1].Picture = bytes;
            return true;
        }
        if (p.Display == "block")
        {
            FbLayoutBlockChild(fb, parent, el, st, p, cx, cw, ref cursor, bfc, flow);
            parent.Kids[^1].Picture = bytes;
            return true;
        }
        var box = new FbBox { Node = el, St = st, P = p, IsBfc = true, IsInline = true, Picture = bytes, W = p.WidthPt + p.ChromeH, H = p.HeightPt + p.ChromeV };
        parent.Kids.Add(box);
        flow.Run.Add(new FbItem { Box = box, Adv = box.OuterW, Above = box.OuterH, Below = 0, St = st });
        return true;
    }

    /// <summary>Stamp a loaded picture over its content box, on the sheet its top falls on.</summary>
    private static void FbStampPicture(FbState fb, FbBox box)
    {
        var (page, py) = FbPageOf(fb, box.ContentY);
        if (page >= fb.pageObjs.Count || box.P.WidthPt <= 0 || box.P.HeightPt <= 0) return;
        try
        {
            var stamp = ImageStamp.FromEncodedBytes(box.Picture!);
            stamp.XIndent = box.ContentX;
            stamp.YIndent = fb.pageH - py - box.P.HeightPt;
            stamp.DisplayWidth = box.P.WidthPt;
            stamp.DisplayHeight = box.P.HeightPt;
            stamp.ApplyTo(fb.pageObjs[page]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or FormatException or System.IO.IOException)
        {
            // an undecodable picture leaves its box empty
        }
    }

    /// <summary>What a box of the column claim paints beside its lines: a loaded picture is queued
    /// for its stamp, a list item draws its marker.</summary>
    private static void FbPaintColumnExtras(FbState fb, FbBox box)
    {
        if (box.Picture is not null) fb.pictures.Add(box);
        if (box.Node is { Tag: "li" }) FbPaintMarker(fb, box);
    }

    /// <summary>A list item's marker, outside its content edge on its first baseline: a bullet, or
    /// the item's number in an ordered list; none when the list style says so.</summary>
    private static void FbPaintMarker(FbState fb, FbBox box)
    {
        var li = box.Node!;
        var style = (FbDecl(fb, li, "list-style-type") ?? FbDecl(fb, li, "list-style") ?? "").Trim().ToLowerInvariant();
        if (style.Length == 0 && li.Parent is { Tag.Length: > 0 } list)
            style = (FbDecl(fb, list, "list-style-type") ?? FbDecl(fb, list, "list-style") ?? "").Trim().ToLowerInvariant();
        if (style.Contains("none") || box.P.Display is "block" or "inline-block" or "flex" || FbFirstBaseline(box) is not { } baseline) return;
        var text = FbBulletMarker;
        if (li.Parent is { Tag: "ol" } ordered)
        {
            var number = 0;
            foreach (var sibling in ordered.Children)
            {
                if (sibling.Tag == "li") number++;
                if (ReferenceEquals(sibling, li)) break;
            }
            text = number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
        }
        FbText(fb, box.St, box.ContentX - FbMeasure(box.St, text) - FbMarkerGapEm * box.St.Pt, baseline, text);
    }

    /// <summary>The baseline (continuous y) of a box's first line, through its first in-flow child; null without one.</summary>
    private static double? FbFirstBaseline(FbBox box)
    {
        double? first = box.Lines.Count > 0 ? box.Lines[0].Y + box.Lines[0].Above : null;
        foreach (var k in box.Kids)
        {
            if (k.IsFloat || k.IsInline) continue;
            if (FbFirstBaseline(k) is { } kb && (first is null || kb < first)) first = kb;
            break;
        }
        return first;
    }
}
