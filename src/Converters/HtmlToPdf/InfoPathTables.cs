using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A laid-out cell of a fixed table: its column span, borders, alignment and content.</summary>
    private sealed class IpCell
    {
        public IpNode Node = null!;
        public int Col, Span = 1;
        public bool Top, Right, Bottom, Left;               // solid 1pt borders
        public bool VAlignTop;
        public string? FillRgb;
        public double ContentH;
    }

    /// <summary>Lay a fixed-layout table out at (x, y) with its declared width, painting it when
    /// asked; returns the height the flow advances by (a bordered table ends at its last grid
    /// line, half a border inside its box).</summary>
    private static double IpLayoutTable(InfoPathState ip, IpNode table, double x, double y, double w, bool paint)
    {
        var cols = new List<double>();
        foreach (var cg in table.Elements("colgroup"))
            foreach (var col in cg.Elements("col")) cols.Add(IpPx(col.Css("width")));
        foreach (var col in table.Elements("col")) cols.Add(IpPx(col.Css("width")));
        if (cols.Count == 0) cols.Add(w);
        var bordered = table.HasClass("msoUcTable") || IpAnyCellBorder(table);
        var half = bordered ? IpHalfBorder : 0;
        var gridX = new double[cols.Count + 1];
        gridX[0] = x + half;
        for (var i = 0; i < cols.Count; i++) gridX[i + 1] = gridX[i] + cols[i];
        var rows = new List<(IpNode tr, List<IpCell> cells, double h)>();
        var bands = new List<(IpNode group, int first, int count)>();
        foreach (var group in table.Children)
        {
            if (group.IsText) continue;
            var trs = group.Tag == "tr" ? new List<IpNode> { group } : new List<IpNode>(group.Elements("tr"));
            var first = rows.Count;
            foreach (var tr in trs) rows.Add((tr, IpRowCells(table, tr, cols.Count), 0));
            if (group.Tag == "tbody" && (group.Css("background-color").Length > 0 || group.HasClass("xdTableHeader")))
                bands.Add((group, first, rows.Count - first));
        }
        // measure: a row is its tallest cell's content plus the pads (and a border when collapsed)
        for (var r = 0; r < rows.Count; r++)
        {
            var (tr, cells, _) = rows[r];
            double contentH = 0;
            foreach (var cell in cells)
            {
                var cw = gridX[cell.Col + cell.Span] - gridX[cell.Col] - IpSideInset(cell.Left) - IpSideInset(cell.Right);
                cell.ContentH = IpLayoutBlocks(ip, cell.Node, 0, 0, cw, false, IpAlignOf(cell.Node));
                contentH = Math.Max(contentH, cell.ContentH);
            }
            rows[r] = (tr, cells, contentH + 2 * IpCellPad + (bordered ? IpBorder : 0));
        }
        var gridY = new double[rows.Count + 1];
        gridY[0] = y + half;
        for (var r = 0; r < rows.Count; r++) gridY[r + 1] = gridY[r] + rows[r].h;
        var height = gridY[^1] - y;
        if (!paint) return height;
        IpPaintBands(ip, table, rows, bands, gridX, gridY);
        IpPaintCellFills(ip, rows, gridX, gridY);
        if (bordered) IpPaintGridEdges(ip, rows, gridX, gridY);
        for (var r = 0; r < rows.Count; r++)
            foreach (var cell in rows[r].cells)
            {
                // a side without a border keeps no half-border space: its content starts at the pad
                var x0 = gridX[cell.Col] + IpSideInset(cell.Left);
                var x1 = gridX[cell.Col + cell.Span] - IpSideInset(cell.Right);
                var top = gridY[r] + IpSideInset(cell.Top);
                var bottom = gridY[r + 1] - IpSideInset(cell.Bottom);
                var seat = cell.VAlignTop ? 0 : (bottom - top - cell.ContentH) / 2;
                IpLayoutBlocks(ip, cell.Node, x0, top + seat, x1 - x0, true, IpAlignOf(cell.Node));
            }
        return height;
    }

    /// <summary>A cell side's inset to its content: the pad, plus half the collapsed border when the side has one.</summary>
    private static double IpSideInset(bool bordered) => IpCellPad + (bordered ? IpHalfBorder : 0);

    private static bool IpAnyCellBorder(IpNode table)
    {
        foreach (var group in table.Children)
        {
            if (group.IsText) continue;
            var trs = group.Tag == "tr" ? new List<IpNode> { group } : new List<IpNode>(group.Elements("tr"));
            foreach (var tr in trs)
                foreach (var td in tr.Elements("td"))
                    if (IpSolidBorder(td, "top") || IpSolidBorder(td, "left") || IpSolidBorder(td, "bottom") || IpSolidBorder(td, "right")) return true;
        }
        return false;
    }

    /// <summary>Whether a cell's inline style declares a solid border on a side (`#000000 1pt
    /// solid`); a colour and width without a style is no border.</summary>
    private static bool IpSolidBorder(IpNode td, string side)
    {
        var v = td.Css("border-" + side);
        if (v.Length > 0) return v.Contains("solid", StringComparison.OrdinalIgnoreCase);
        var st = td.Css("border-" + side + "-style");
        if (st.Length > 0) return st.Equals("solid", StringComparison.OrdinalIgnoreCase);
        var all = td.Css("border");
        return all.Contains("solid", StringComparison.OrdinalIgnoreCase);
    }

    private static List<IpCell> IpRowCells(IpNode table, IpNode tr, int colCount)
    {
        var cells = new List<IpCell>();
        var mso = table.HasClass("msoUcTable");
        var repeating = table.HasClass("xdRepeatingTable");
        var col = 0;
        foreach (var td in tr.Elements("td"))
        {
            if (col >= colCount) break;
            var span = int.TryParse(td.Attr("colspan"), out var cs) && cs > 0 ? cs : 1;
            span = Math.Min(span, colCount - col);
            var valign = td.Css("vertical-align");
            if (valign.Length == 0) valign = td.Attr("valign");
            var cell = new IpCell
            {
                Node = td, Col = col, Span = span,
                Top = mso || IpSolidBorder(td, "top"), Right = mso || IpSolidBorder(td, "right"),
                Bottom = mso || IpSolidBorder(td, "bottom"), Left = mso || IpSolidBorder(td, "left"),
                VAlignTop = valign.Equals("top", StringComparison.OrdinalIgnoreCase) || (repeating && valign.Length == 0),
                FillRgb = IpRgb(td.Css("background-color")),
            };
            cells.Add(cell);
            col += span;
        }
        return cells;
    }

    private static bool IpAlignOf(IpNode el) => el.Attr("align").Equals("center", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lay a container's children out as blocks at (x, y) in a width: a run of inline
    /// content is one line, a div or a table its own block; wrappers (font, strong, span) are
    /// transparent to the blocks inside them. Returns the stack's height.</summary>
    private static double IpLayoutBlocks(InfoPathState ip, IpNode container, double x, double y, double w, bool paint, bool center)
    {
        var st = IpStyleOf(container, IpInheritedStyle(container));
        var inline = new List<(IpNode node, IpStyle style)>();
        var cursor = y;
        IpWalkBlocks(ip, container, st, x, ref cursor, w, paint, center, inline);
        cursor += IpFlushLine(ip, inline, x, cursor, w, paint, center);
        return cursor - y;
    }

    private static void IpWalkBlocks(InfoPathState ip, IpNode el, IpStyle st, double x, ref double cursor, double w, bool paint, bool center, List<(IpNode node, IpStyle style)> inline)
    {
        foreach (var c in el.Children)
        {
            if (c.IsText) { inline.Add((c, st)); continue; }
            if (IpHidden(c)) continue;
            switch (c.Tag)
            {
                case "table":
                {
                    cursor += IpFlushLine(ip, inline, x, cursor, w, paint, center);
                    var tw = IpPx(c.Css("width"));
                    if (tw <= 0) tw = w;
                    var tx = center ? x + (w - tw) / 2 : x;
                    cursor += IpLayoutTable(ip, c, tx, cursor, tw, paint);
                    break;
                }
                case "div" when !c.HasClass("xdDTPicker"):
                {
                    cursor += IpFlushLine(ip, inline, x, cursor, w, paint, center);
                    var padL = c.HasClass("optionalPlaceholder") ? IpPlaceholderPad : IpPx(c.Css("padding-left"));
                    var padR = c.HasClass("optionalPlaceholder") ? IpPlaceholderPad : IpPx(c.Css("padding-right"));
                    var childInline = new List<(IpNode node, IpStyle style)>();
                    var childStyle = IpStyleOf(c, st);
                    var childCenter = IpAlignOf(c);
                    IpWalkBlocks(ip, c, childStyle, x + padL, ref cursor, w - padL - padR, paint, childCenter, childInline);
                    cursor += IpFlushLine(ip, childInline, x + padL, cursor, w - padL - padR, paint, childCenter);
                    break;
                }
                case "select": case "input": case "button": case "img":
                    inline.Add((c, st));
                    break;
                case "span" when c.HasClass("xdTextBox") || c.HasClass("xdRichTextBox") || c.HasClass("xdHyperlinkBox"):
                case "div":
                    inline.Add((c, st));
                    break;
                case "colgroup": case "col": case "option":
                    break;
                default:
                    IpWalkBlocks(ip, c, IpStyleOf(c, st), x, ref cursor, w, paint, center, inline);
                    break;
            }
        }
    }

    /// <summary>Close the pending inline run as one line; returns its height (0 when empty).</summary>
    private static double IpFlushLine(InfoPathState ip, List<(IpNode node, IpStyle style)> inline, double x, double top, double w, bool paint, bool center)
    {
        if (inline.Count == 0) return 0;
        var line = IpBuildLine(ip, inline, w);
        inline.Clear();
        if (line.Items.Count == 0) return 0;
        if (paint) IpPaintLine(ip, line, center ? x + (w - line.Width) / 2 : x, top);
        return line.Height;
    }

    /// <summary>The text state a container inherits from its ancestors (sizes, weight, colour,
    /// the title band's light text), walked from the body down.</summary>
    private static IpStyle IpInheritedStyle(IpNode el)
    {
        var chain = new List<IpNode>();
        for (var p = el.Parent; p is not null; p = p.Parent) chain.Add(p);
        var st = new IpStyle();
        for (var i = chain.Count - 1; i >= 0; i--) st = IpStyleOf(chain[i], st);
        return st;
    }
}
