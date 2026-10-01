using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A block whose element children are all inline-blocks of declared percent
    /// width: one line of side-by-side boxes whose last text lines share a baseline.</summary>
    internal sealed class InlineRow
    {
        public List<InlineRowCell> Cells = new();
        // A display:table row: its cells stack their lines from the row's top (vertical-align
        // top) instead of sharing a last-line baseline, and the row is at least this tall.
        public bool TopAligned;
        public double MinHeightPt;
        public double PadTopPt;          // the container's padding-top (a section heading's 1 em)
        public double PadBottomPt;       // ...and padding-bottom
        public double HostLeftPt;        // the container's own padding-left + margin-left
        public double HostRightPt;       // ...and padding-right + margin-right
    }

    internal sealed class InlineRowCell
    {
        public double WidthFrac;         // declared percent width / 100
        public double FontPt = UaDefaultFontPt;
        public bool Bold;
        public bool Italic;
        public Color? Color;
        public string Text = "";
        // `text-align` inside the cell's box: 0 left, 1 centre, 2 right.
        public int Align;
        // A `margin-left` on the item moves its box right and advances the row by as much.
        public double MarginLeftPt;
        // A table cell's block children, one line each in its own typography (null: the cell is
        // the single Text run above).
        public List<InlineRowLine>? Lines;
    }

    internal sealed class InlineRowLine
    {
        public string Text = "";
        public double FontPt = UaDefaultFontPt;
        public bool Bold, Italic;
        public Color? Color;
    }

    // Inline-block row law (measured on the claim-confirmation form against the
    // reference): each item is a box of its percent share of the UA content box; its text wraps
    // inside the box on the UA serif at the item's class size (13px bold headers = 9.75);
    // the items align on their LAST line's baseline, the row's baseline standing the tallest
    // item's ascent below the row top, and the row ends at the deepest item's line-box
    // bottom. The whitespace between two inline-blocks is one space glyph at the start of the
    // following item. A container's padding stands above and below the row (the section
    // heading's `padding: 1em 0 .5em`).
    private const string InlineRowFace = "Times New Roman";
    private const string InlineRowBoldFace = "Times New Roman Bold";
    private const string InlineRowItalicFace = "Times New Roman Italic";
    private const string InlineRowBoldItalicFace = "Times New Roman Bold Italic";
    private const int InlineRowAlignLeft = 0, InlineRowAlignCenter = 1, InlineRowAlignRight = 2;

    /// <summary>A div whose element children are all percent-width inline-blocks holding
    /// inline text becomes an inline-row block.</summary>
    private static void ScanInlineRows(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "div" || Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
            var block = BuildTableRowBlock(el, rb.css) ?? BuildInlineRowBlock(el, rb.css);
            if (block is not null) rb.extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }
    }

    private static Block? BuildInlineRowBlock(HtmlNode host, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        // (the items' own style attributes declare them just as well as a sheet does)
        var row = new InlineRow();
        var hostFontPt = DomFontPx(host, 16, css) * PxToPt;
        var hostColor = DomColor(host, css);
        var hostBold = DomBold(host, css);
        var pendingSpace = false;
        foreach (var item in host.Children)
        {
            if (item.Tag.Length == 0)
            {
                if (item.Text.Trim().Length > 0) return null;
                pendingSpace |= item.Text.Length > 0;
                continue;
            }
            if (item.Tag != "div" || IsHiddenElement(item.Tag, item.Attrs, css)) return null;
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_BLOCKS") == "1")
                Console.WriteLine($"[inlinerow] host class='{(host.Attrs is not null && host.Attrs.TryGetValue("class", out var hc) ? hc : "")}' item class='{(item.Attrs is not null && item.Attrs.TryGetValue("class", out var ic) ? ic : "")}' display='{DomDecl(item, "display", css)}' width='{DomDecl(item, "width", css)}'");
            if (DomDecl(item, "display", css)?.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase) != true) return null;
            if (DomDecl(item, "position", css)?.Trim().Equals("absolute", StringComparison.OrdinalIgnoreCase) == true) return null;
            foreach (var d in item.Descendants())
                if (d.Tag.Length > 0 && !InlineRowTags.Contains(d.Tag)) return null;
            if (DomDecl(item, "width", css)?.Trim() is not { } w
                || Regex.Match(w, @"^([\d.]+)\s*%$") is not { Success: true } pm
                || !double.TryParse(pm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct) || pct <= 0)
                return null;
            var cell = new InlineRowCell { WidthFrac = pct / 100.0, Text = DomText(item, css), FontPt = hostFontPt };
            if (row.Cells.Count > 0 && pendingSpace && cell.Text.Length > 0) cell.Text = " " + cell.Text;
            pendingSpace = false;
            if (DomDecl(item, "font-size", css) is { } fsv && TryParseLength(fsv.Trim()) is { } fsPt && fsPt > 0) cell.FontPt = fsPt;
            // weight, colour and slant inherit from the container when the item declares none
            cell.Bold = DomDecl(item, "font-weight", css) is { } fw
                ? fw.Trim().StartsWith("bold", StringComparison.OrdinalIgnoreCase) || (int.TryParse(fw.Trim(), out var fwN) && fwN >= 600)
                : hostBold;
            cell.Italic = DomDecl(item, "font-style", css)?.Trim().Equals("italic", StringComparison.OrdinalIgnoreCase) == true;
            cell.Color = DomDecl(item, "color", css) is { } col ? ParseCssColor(col) : hostColor;
            cell.Align = DomDecl(item, "text-align", css)?.Trim().ToLowerInvariant() switch
            {
                "right" => InlineRowAlignRight,
                "center" => InlineRowAlignCenter,
                _ => InlineRowAlignLeft,
            };
            if (DomDecl(item, "margin-left", css) is { } mlv && TryParseLength(mlv.Trim()) is { } mlPt && mlPt > 0) cell.MarginLeftPt = mlPt;
            row.Cells.Add(cell);
        }
        if (row.Cells.Count == 0) return null;
        var (padTop, padBottom) = InlineRowPadding(host, css);
        row.PadTopPt = padTop;
        row.PadBottomPt = padBottom;
        var (padL, padR) = DomBoxLR(host, "padding", css);
        var (mL, mR) = DomBoxLR(host, "margin", css);
        row.HostLeftPt = (padL + mL) * PxToPt;
        row.HostRightPt = (padR + mR) * PxToPt;
        return new Block { Text = "", InlineRow = row };
    }

    /// <summary>The container's vertical padding (shorthand and longhands) in points, an em
    /// being the UA base size (the section heading's `padding: 1em 0 .5em 0` = 12 above, 6 below).</summary>
    private static (double top, double bottom) InlineRowPadding(HtmlNode el, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        static double Pt(string v)
        {
            v = v.Trim();
            if (Regex.Match(v, @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase) is { Success: true } em
                && double.TryParse(em.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ems))
                return ems * UaDefaultFontPt;
            return TryParseLength(v) ?? 0;
        }
        double top = 0, bottom = 0;
        if (DomDecl(el, "padding", css) is { } shorthand)
        {
            var parts = shorthand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                top = Pt(parts[0]);
                bottom = Pt(parts.Length >= 3 ? parts[2] : parts[0]);
            }
        }
        if (DomDecl(el, "padding-top", css) is { } t) top = Pt(t);
        if (DomDecl(el, "padding-bottom", css) is { } b) bottom = Pt(b);
        return (top, bottom);
    }

    /// <summary>The face an inline-row cell draws in: the row's serif, bold and/or italic.</summary>
    private static string InlineRowCellFace(InlineRowCell c, bool boldAvailable) => (c.Bold && boldAvailable, c.Italic) switch
    {
        (true, true) => InlineRowBoldItalicFace,
        (true, false) => InlineRowBoldFace,
        (false, true) => InlineRowItalicFace,
        _ => InlineRowFace,
    };

    /// <summary>Lay the inline-block row out at the flow cursor.</summary>
    private static void LayoutInlineRow(ConvertState cv, InlineRow row, Block block)
    {
        var regular = PosFace(InlineRowFace);
        var bold = PosFace(InlineRowBoldFace);
        var hhea = HheaLineSumFor(InlineRowFace);
        var win = WinMetricsFor(InlineRowFace);
        if (regular.ttf is null || hhea is null || win is null) return;
        if (row.TopAligned) { LayoutTableRow(cv, row, block, regular.ttf, hhea.Value, win.Value); return; }
        // The row is its container's content box: the flow's width less the body inset
        // on the right and the insets the container's ancestors carry (the reward letter's
        // rows: 449.4 = 68 % of the statement box, less the section's padding and margin).
        var rowW = (block.LeftIndent > 0 || block.RightInsetPt > 0
            ? cv.flow.contentWidth - UaBodyMarginPt - block.LeftIndent - block.RightInsetPt
            : cv.pageWidth - 2 * cv.marginLeft) - row.HostLeftPt - row.HostRightPt;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") == "1")
            Console.WriteLine(FormattableString.Invariant($"[inlinerow] li={block.LeftIndent:0.##} ri={block.RightInsetPt:0.##} hostL={row.HostLeftPt:0.##} hostR={row.HostRightPt:0.##} cw={cv.flow.contentWidth:0.##} rowW={rowW:0.##}"));
        var n = row.Cells.Count;
        var lines = new string[n][];
        var lineH = new double[n];
        var drop = new double[n];
        double above = 0, below = 0;
        for (var i = 0; i < n; i++)
        {
            var c = row.Cells[i];
            var face = InlineRowCellFace(c, bold.ttf is not null);
            lineH[i] = MetricLineHeight(c.FontPt, hhea.Value);
            drop[i] = MetricBaselineDrop(c.FontPt, lineH[i], win.Value);
            lines[i] = c.Text.Length > 0
                ? MeasuredWordWrap(c.Text.TrimStart(), c.WidthFrac * rowW + WrapEqualityEpsilonPt, face, c.FontPt, wordFirst: true)
                : Array.Empty<string>();
            var k = Math.Max(1, lines[i].Length);
            above = Math.Max(above, (k - 1) * lineH[i] + drop[i]);
            below = Math.Max(below, lineH[i] - drop[i]);
        }
        var boxH = row.PadTopPt + above + below + row.PadBottomPt;
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
        var baseline = top - row.PadTopPt - above;
        var x = cv.marginLeft + block.LeftIndent + row.HostLeftPt;
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            var c = row.Cells[i];
            var faceName = InlineRowCellFace(c, bold.ttf is not null);
            var ttf = PosFace(faceName).ttf ?? regular.ttf;
            var k = lines[i].Length;
            var cellW = c.WidthFrac * rowW;
            x += c.MarginLeftPt;
            // The collapsed inter-block whitespace is a space inline of its own at the item's
            // left edge: the item's text - every wrapped line of it - starts after it
            // (measured: a two-line value seats both lines 3 pt in, the space at the edge).
            var textX = x + (c.Text.StartsWith(' ') ? MeasureFaceText(faceName, " ", c.FontPt) : 0);
            for (var li = 0; li < k && fontDict is not null; li++)
            {
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, faceName, lines[i][li], stripSpacesInBaseFont: true);
                var rgb = c.Color is { } col ? $"{col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg" : "0 0 0 rg";
                // a right- or centre-aligned line seats against its box's far edge or middle
                var lineX = c.Align == InlineRowAlignLeft ? textX
                    : c.Align == InlineRowAlignRight ? x + cellW - MeasureFaceText(faceName, lines[i][li], c.FontPt)
                    : x + (cellW - MeasureFaceText(faceName, lines[i][li], c.FontPt)) / 2;
                sb.Append(Compat.Format(inv,
                    $"BT {rgb} /{rn} {c.FontPt:F2} Tf 1 0 0 1 {lineX:F3} {baseline + (k - 1 - li) * lineH[i]:F3} Tm <{Compat.ToHexString(hex)}> Tj ET\n"));
            }
            x += cellW;
        }
        cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        cv.flow.y = top - boxH;
        cv.flow.lastWasHardBreak = false;
        // the row is flow content: the page-top margin collapse is spent on it, so the
        // first paragraph after a run of rows keeps its own top margin (measured: the
        // claim form's blank paragraphs seat 26.94 apart from the last row, not 13.5)
        cv.flow.uaTopMarginPending = false;
    }
}
