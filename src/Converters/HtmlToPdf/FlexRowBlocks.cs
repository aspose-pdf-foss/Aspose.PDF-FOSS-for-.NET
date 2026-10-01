using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A single-line flex container: one row of painted boxes whose widths come from
    /// the items' declared percent widths and their <c>flex</c> grow factors.</summary>
    internal sealed class FlexRow
    {
        public List<FlexRowCell> Cells = new();
    }

    internal sealed class FlexRowCell
    {
        public double WidthFrac;      // declared percent width / 100 (0 = a flexible item)
        public double Grow;           // the `flex: n` grow factor
        public Color? Background;
        public string Text = "";
    }

    // Flex-row law (probed against the reference, checked against the expected render): a
    // `display: flex` row lays its items out on ONE line of the UA serif face. An item with a
    // percent width takes that share of the content box; every other item's basis is its text's
    // advance plus the one collapsed space the engine keeps ahead of it (the reference has since moved
    // to the longest word; the expected render measures the whole text), and what the row has
    // left splits over those items in proportion to their `flex` grow factors (an empty item's
    // basis is zero). Every box paints its background over the row's line boxes - the tallest
    // item's wrapped line count - and seats its text at the box's left edge with no padding.
    private const double FlexRowFontPt = UaDefaultFontPt;
    private const string FlexRowFace = "Times New Roman";
    private const double FlexRowBasisLeadingSpaceEm = 0.25;
    // The legacy flow's page-top cursor sits its default first line (11 pt) below the body edge.
    private const double FlexRowFirstLineSeatPt = 11.0;

    /// <summary>A <c>display: flex</c> row container whose items carry percent widths or flex
    /// factors and only inline content becomes a flex-row block.</summary>
    private static void ScanFlexRows(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "div" || Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
            if (DomDecl(el, "display", rb.css)?.Trim()
                    .Equals("flex", StringComparison.OrdinalIgnoreCase) != true) continue;
            var flow = DomDecl(el, "flex-flow", rb.css) ?? DomDecl(el, "flex-direction", rb.css);
            if (flow is not null && !flow.TrimStart().StartsWith("row", StringComparison.OrdinalIgnoreCase)) continue;
            var block = BuildFlexRowBlock(el, rb.css);
            if (block is not null) rb.extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }
    }

    /// <summary>Read the row's items: each element child is one box (a percent width or a flex
    /// factor, its background and its collapsed text); bare text or block content declines.</summary>
    private static Block? BuildFlexRowBlock(HtmlNode host, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var row = new FlexRow();
        var anyGrow = false;
        foreach (var item in host.Children)
        {
            if (item.Tag.Length == 0)
            {
                if (item.Text.Trim().Length > 0) return null;
                continue;
            }
            if (IsHiddenElement(item.Tag, item.Attrs, css)) continue;
            foreach (var d in item.Descendants())
                if (d.Tag.Length > 0 && !InlineRowTags.Contains(d.Tag)) return null;
            var cell = new FlexRowCell { Text = DomText(item, css) };
            if (DomDecl(item, "width", css)?.Trim() is { } w
                && Regex.Match(w, @"^([\d.]+)\s*%$") is { Success: true } pm
                && double.TryParse(pm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                cell.WidthFrac = pct / 100.0;
            if ((DomDecl(item, "flex", css) ?? DomDecl(item, "flex-grow", css))?.Trim() is { } flex
                && Regex.Match(flex, @"^([\d.]+)") is { Success: true } fm
                && double.TryParse(fm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var grow))
            {
                cell.Grow = grow;
                anyGrow |= grow > 0;
            }
            if (cell.WidthFrac <= 0 && cell.Grow <= 0) return null;
            cell.Background = DomDecl(item, "background-color", css) is { } bg ? ParseCssColor(bg) : null;
            row.Cells.Add(cell);
        }
        return row.Cells.Count >= 2 && anyGrow ? new Block { Text = "", FlexRow = row } : null;
    }

    /// <summary>Lay the flex row out at the flow cursor: solve the box widths, wrap each item's
    /// text in its box, paint the backgrounds over the row's line boxes and seat the text.</summary>
    private static void LayoutFlexRow(ConvertState cv, FlexRow row)
    {
        var face = PosFace(FlexRowFace);
        var win = WinMetricsFor(FlexRowFace);
        if (face.ttf is null || win is null) return;
        var lineH = MetricLineHeight(FlexRowFontPt, win.Value.sum);
        var drop = MetricBaselineDrop(FlexRowFontPt, lineH, win.Value);
        // the UA body box: the page margin and body inset on BOTH sides, whatever right
        // margin the flow's dialect keeps for its own text
        var rowW = cv.pageWidth - 2 * cv.marginLeft;

        var n = row.Cells.Count;
        var basis = new double[n];
        double fixedSum = 0, basisSum = 0, growSum = 0;
        for (var i = 0; i < n; i++)
        {
            var c = row.Cells[i];
            if (c.WidthFrac > 0) { basis[i] = c.WidthFrac * rowW; fixedSum += basis[i]; continue; }
            if (c.Text.Length > 0)
                basis[i] = MeasureFaceText(FlexRowFace, c.Text, FlexRowFontPt)
                    + FlexRowBasisLeadingSpaceEm * FlexRowFontPt;
            basisSum += basis[i];
            growSum += c.Grow;
        }
        var free = Math.Max(0, rowW - fixedSum - basisSum);
        var widths = new double[n];
        var lines = new string[n][];
        var rowLines = 1;
        for (var i = 0; i < n; i++)
        {
            var c = row.Cells[i];
            widths[i] = c.WidthFrac > 0 ? basis[i]
                : basis[i] + (growSum > 0 ? free * c.Grow / growSum : 0);
            lines[i] = c.Text.Length > 0
                ? MeasuredWordWrap(c.Text, widths[i] + WrapEqualityEpsilonPt, FlexRowFace, FlexRowFontPt, wordFirst: true)
                : Array.Empty<string>();
            rowLines = Math.Max(rowLines, lines[i].Length);
        }
        var boxH = rowLines * lineH;
        if (cv.flow.y - boxH < cv.marginBottom && cv.flow.y < cv.pageHeight - cv.marginTop - 1e-3)
        {
            cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
            EnsureFonts(cv.flow.page, cv.docFontDict);
            cv.flow.y = cv.pageHeight - cv.marginTop;
        }
        var fontDict = cv.flow.page.Dict.Get("Resources") is Core.PdfDictionary res
            ? res.Get("Font") as Core.PdfDictionary : null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // At the page top the flow cursor already stands one legacy first line (the
        // calibrated 89 pt seat) below the body's top edge: the row's box opens at the edge.
        var bodyTopEdge = cv.marginsExplicit ? cv.marginTop + UaBodyMarginPt : cv.marginTop - FlexRowFirstLineSeatPt;
        var pageTopY = cv.pageHeight - bodyTopEdge;
        var top = cv.flow.y >= cv.pageHeight - cv.marginTop - 1e-3 ? pageTopY : cv.flow.y;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_FLEXROW") == "1")
            Console.WriteLine($"[flexrow] flow.y={cv.flow.y:0.##} pageTopY={pageTopY:0.##} top={top:0.##} marginLeft={cv.marginLeft:0.##} marginRight={cv.marginRight:0.##} rowW={rowW:0.##} pendingTopDrop={cv.flow.pendingTopDrop}");
        var x = cv.marginLeft;
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            if (row.Cells[i].Background is { } bg)
                sb.Append(Compat.Format(inv,
                    $"q {bg.R / 255.0:0.###} {bg.G / 255.0:0.###} {bg.B / 255.0:0.###} rg {x:F3} {top - boxH:F3} {widths[i]:F3} {boxH:F3} re f Q\n"));
            if (fontDict is not null)
                for (var li = 0; li < lines[i].Length; li++)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf, FlexRowFace, lines[i][li],
                        stripSpacesInBaseFont: true);
                    sb.Append(Compat.Format(inv,
                        $"BT 0 0 0 rg /{rn} {FlexRowFontPt:F1} Tf 1 0 0 1 {x:F3} {top - drop - li * lineH:F3} Tm <{Compat.ToHexString(hex)}> Tj ET\n"));
                }
            x += widths[i];
        }
        cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        cv.flow.y = top - boxH;
        cv.flow.lastWasHardBreak = false;
    }

    // A column sized to its own max-content must not wrap on the equality boundary.
    private const double WrapEqualityEpsilonPt = 0.05;

    /// <summary>The verbatim-geometry arms: a flex row of painted boxes, a preformatted block.
    /// False when one of them laid the block out; null to let the other arms try.</summary>
    private static bool? RenderVerbatimBlockArms(ConvertState cv, Block block)
    {
        if (block.FlexRow is { } flexRow) { LayoutFlexRow(cv, flexRow); return false; }
        if (block.Pre is { } preBlock) { LayoutPreBlock(cv, preBlock); return false; }
        if (block.InlineRow is { } inlineRow) { LayoutInlineRow(cv, inlineRow, block); return false; }
        return null;
    }
}
