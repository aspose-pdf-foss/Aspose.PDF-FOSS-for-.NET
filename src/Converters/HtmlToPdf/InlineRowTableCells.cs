using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A `display: table` div whose element children are `display: table-cell` divs is a
    /// row of cells: each takes its declared percent of the row (the unsized ones share what is
    /// left), its block children stand one line each from the row's top in their own size, weight,
    /// slant and ink, seated by the cell's text-align (probed on the reward letter's banner: the
    /// 30 % / 30 % / rest cells put "Alexander Burgess", the centred "April 2020" and the
    /// right-aligned "New Salary" on ONE line at 76.23, the cells' further lines below).</summary>
    private static Block? BuildTableRowBlock(HtmlNode host, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        if (DomDecl(host, "display", css)?.Trim().Equals("table", StringComparison.OrdinalIgnoreCase) != true) return null;
        var row = new InlineRow { TopAligned = true };
        var unsized = 0;
        foreach (var item in host.Children)
        {
            if (item.Tag.Length == 0)
            {
                if (item.Text.Trim().Length > 0) return null;
                continue;
            }
            if (IsHiddenElement(item.Tag, item.Attrs, css)) continue;
            if (item.Tag != "div"
                || DomDecl(item, "display", css)?.Trim().Equals("table-cell", StringComparison.OrdinalIgnoreCase) != true)
                return null;
            var cell = new InlineRowCell { Lines = new List<InlineRowLine>() };
            if (DomDecl(item, "width", css)?.Trim() is { } w
                && Regex.Match(w, @"^([\d.]+)\s*%$") is { Success: true } pm
                && double.TryParse(pm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 0)
                cell.WidthFrac = pct / 100.0;
            else unsized++;
            cell.Align = DomDecl(item, "text-align", css)?.Trim().ToLowerInvariant() switch
            {
                "right" => InlineRowAlignRight,
                "center" => InlineRowAlignCenter,
                _ => InlineRowAlignLeft,
            };
            CollectTableCellLines(item, item, css, cell.Lines);
            row.Cells.Add(cell);
        }
        if (row.Cells.Count < 2) return null;
        var declared = 0.0;
        foreach (var c in row.Cells) declared += c.WidthFrac;
        if (declared > 1 + 1e-6) return null;
        if (unsized > 0)
            foreach (var c in row.Cells)
                if (c.WidthFrac <= 0) c.WidthFrac = (1 - declared) / unsized;
        var (padTop, padBottom) = InlineRowPadding(host, css);
        row.PadTopPt = padTop;
        row.PadBottomPt = padBottom;
        var (padL, padR) = DomBoxLR(host, "padding", css);
        var (mL, mR) = DomBoxLR(host, "margin", css);
        row.HostLeftPt = (padL + mL) * PxToPt;
        row.HostRightPt = (padR + mR) * PxToPt;
        row.MinHeightPt = ParsePxValue(DomDecl(host, "height", css)) * PxToPt;
        return new Block { Text = "", InlineRow = row };
    }

    /// <summary>A cell's lines: a bare text node, or an element child, is one line in the
    /// typography it inherits (an element that itself holds only block children contributes
    /// theirs); inline-block children on one line stay one line.</summary>
    private static void CollectTableCellLines(HtmlNode cell, HtmlNode node,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css, List<InlineRowLine> lines)
    {
        foreach (var ch in node.Children)
        {
            if (ch.Tag.Length == 0)
            {
                var t = CollapseWs(DecodeEntities(ch.Text)).Trim();
                if (t.Length > 0) lines.Add(TableCellLine(node, t, css));
                continue;
            }
            if (IsHiddenElement(ch.Tag, ch.Attrs, css)) continue;
            var blockChildren = false;
            foreach (var g in ch.Children)
                if (g.Tag == "div" && DomDecl(g, "display", css)?.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase) != true)
                { blockChildren = true; break; }
            if (blockChildren) { CollectTableCellLines(cell, ch, css, lines); continue; }
            var text = DomText(ch, css).Trim();
            if (text.Length > 0) lines.Add(TableCellLine(ch, text, css));
        }
    }

    private static InlineRowLine TableCellLine(HtmlNode el, string text, IReadOnlyDictionary<string, Dictionary<string, string>>? css) => new()
    {
        Text = text,
        FontPt = DomFontPx(el, 16, css) * PxToPt,
        Bold = DomBold(el, css),
        Italic = DomDecl(el, "font-style", css)?.Trim().Equals("italic", StringComparison.OrdinalIgnoreCase) == true,
        Color = DomColor(el, css),
    };

    /// <summary>Lay a top-aligned row out: every cell stacks its lines from the row's top, each
    /// line on its own normal line box, seated by the cell's alignment; the row is as tall as
    /// its tallest cell (or its declared height), plus the container's padding.</summary>
    private static void LayoutTableRow(ConvertState cv, InlineRow row, Block block, byte[] regularTtf, double hhea, (double asc, double sum) win)
    {
        var rowW = (block.LeftIndent > 0 || block.RightInsetPt > 0
            ? cv.flow.contentWidth - UaBodyMarginPt - block.LeftIndent - block.RightInsetPt
            : cv.pageWidth - 2 * cv.marginLeft) - row.HostLeftPt - row.HostRightPt;
        var n = row.Cells.Count;
        var wrapped = new List<(string text, string face, double fontPt, double lineH, double drop, Color? color, int align)>[n];
        var cellH = new double[n];
        for (var i = 0; i < n; i++)
        {
            var c = row.Cells[i];
            wrapped[i] = new();
            var cellW = c.WidthFrac * rowW;
            foreach (var ln in c.Lines ?? new List<InlineRowLine>())
            {
                var face = InlineRowCellFace(new InlineRowCell { Bold = ln.Bold, Italic = ln.Italic }, PosFace(InlineRowBoldFace).ttf is not null);
                var lineH = MetricLineHeight(ln.FontPt, hhea);
                var drop = MetricBaselineDrop(ln.FontPt, lineH, win);
                foreach (var sub in MeasuredWordWrap(ln.Text, cellW + WrapEqualityEpsilonPt, face, ln.FontPt, wordFirst: true))
                {
                    wrapped[i].Add((sub, face, ln.FontPt, lineH, drop, ln.Color, c.Align));
                    cellH[i] += lineH;
                }
            }
        }
        var contentH = 0.0;
        foreach (var h in cellH) contentH = Math.Max(contentH, h);
        contentH = Math.Max(contentH, row.MinHeightPt - row.PadTopPt - row.PadBottomPt);
        var boxH = row.PadTopPt + contentH + row.PadBottomPt;
        var pageTopY = cv.pageHeight - (cv.marginsExplicit ? cv.marginTop + UaBodyMarginPt : cv.marginTop - FlexRowFirstLineSeatPt);
        var top = cv.flow.y >= cv.pageHeight - cv.marginTop - 1e-3 ? pageTopY : cv.flow.y;
        if (top - boxH < cv.marginBottom && top < pageTopY - 1e-3)
        {
            cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
            EnsureFonts(cv.flow.page, cv.docFontDict);
            top = pageTopY;
        }
        var fontDict = cv.flow.page.Dict.Get("Resources") is Core.PdfDictionary res ? res.Get("Font") as Core.PdfDictionary : null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var x = cv.marginLeft + block.LeftIndent + row.HostLeftPt;
        var sb = new StringBuilder();
        for (var i = 0; i < n && fontDict is not null; i++)
        {
            var cellW = row.Cells[i].WidthFrac * rowW;
            var y = top - row.PadTopPt;
            foreach (var ln in wrapped[i])
            {
                var ttf = PosFace(ln.face).ttf ?? regularTtf;
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, ln.face, ln.text, stripSpacesInBaseFont: true);
                var rgb = ln.color is { } col ? $"{col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg" : "0 0 0 rg";
                var textW = MeasureFaceText(ln.face, ln.text, ln.fontPt);
                var lineX = ln.align == InlineRowAlignRight ? x + cellW - textW
                    : ln.align == InlineRowAlignCenter ? x + (cellW - textW) / 2 : x;
                sb.Append(Compat.Format(inv,
                    $"BT {rgb} /{rn} {ln.fontPt:F2} Tf 1 0 0 1 {lineX:F3} {y - ln.drop:F3} Tm <{Compat.ToHexString(hex)}> Tj ET\n"));
                y -= ln.lineH;
            }
            x += cellW;
        }
        cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        cv.flow.y = top - boxH;
        cv.flow.lastWasHardBreak = false;
        cv.flow.uaTopMarginPending = false;
    }
}
