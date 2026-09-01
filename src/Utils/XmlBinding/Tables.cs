using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static partial class XmlBinding
{
    private static Table BuildTable(XmlNode tableNode, BindContext ctx)
    {
        var table = new Table { XmlGeneratorModel = true, XmlLineSpacing = ctx.Defaults.LineSpacing };
        ConfigureTable(table, tableNode, ctx);
        return table;
    }

    // Shared table parsing: attributes (column widths, repeating header, column
    // adjustment, alignment) plus the table-level styling children (Border,
    // DefaultCellBorder, DefaultCellPadding) and rows. Used by both the page-level
    // ProcessTable and the nested BuildTable so styling is applied consistently.
    private static void ConfigureTable(Table table, XmlNode tableNode, BindContext ctx)
    {
        var colWidths = GetAttr(tableNode, "ColumnWidths");
        if (colWidths is not null) table.ColumnWidths = colWidths;

        // Repeat the first row(s) at the top of every continuation page.
        var repeat = GetAttr(tableNode, "RepeatingRowsCount");
        if (repeat is not null && int.TryParse(repeat, out var rc) && rc > 0)
            table.RepeatingRowsCount = rc;
        if (table.RepeatingRowsCount == 0 &&
            string.Equals(GetAttr(tableNode, "IsFirstRowRepeated"), "true", StringComparison.OrdinalIgnoreCase))
            table.RepeatingRowsCount = 1;

        switch (GetAttr(tableNode, "ColumnAdjustment"))
        {
            case "AutoFitToWindow": table.ColumnAdjustment = ColumnAdjustment.AutoFitToWindow; break;
            case "AutoFitToContent": table.ColumnAdjustment = ColumnAdjustment.AutoFitToContent; break;
        }
        if (ParseHAlign(GetAttr(tableNode, "Alignment")) is { } ta)
            table.Alignment = ta;
        if (ParseColorValue(GetAttr(tableNode, "BackgroundColor")) is { } tbg)
            table.BackgroundColor = tbg;

        foreach (XmlNode child in tableNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Row":
                    ProcessRow(table, child, ctx);
                    break;
                case "Margin":
                    table.Margin = ParseMargin(child);
                    break;
                case "Border":
                    table.Border = ParseBorder(child);
                    break;
                case "DefaultCellBorder":
                    table.DefaultCellBorder = ParseBorder(child);
                    break;
                case "DefaultCellPadding":
                    table.DefaultCellPadding = ParseMargin(child);
                    break;
                case "DefaultCellTextState":
                    table.DefaultCellTextState = ParseTextState(child, table.DefaultCellTextState);
                    break;
            }
        }

        // The document-level DefaultTextState FontSize is the cells' fallback
        // when the table declares none of its own (12 pt cells) — an
        // authored DefaultCellTextState keeps precedence (8 pt cells).
        if (ctx.Defaults.FontSize > 0 && table.DefaultCellTextState is { FontSizeTouched: false } seedDcts)
            seedDcts.FontSize = (float)ctx.Defaults.FontSize;

        // Tables whose cells carry HtmlFragments keep the LEGACY layout dialect:
        // their geometry was calibrated long before the XML-generator model
        // (a nested HTML report), and the model's centring/row rules move
        // their rows off the shipped templates.
        if (table.XmlGeneratorModel)
            foreach (Row xr in table.Rows)
            {
                foreach (Cell xc in xr.Cells)
                {
                    foreach (var xp in xc.Paragraphs)
                        if (xp is HtmlFragment
                            || (xp is Table nested && !nested.XmlGeneratorModel))
                        { table.XmlGeneratorModel = false; break; }
                    if (!table.XmlGeneratorModel) break;
                }
                if (!table.XmlGeneratorModel) break;
            }

        // A stylesheet-bearing HtmlFragment in any cell sets the table's base
        // font size (CSS px, browser 0.75 px→pt): sibling plain-text cells lay
        // out at the same size as the styled HTML content.
        if (table.DefaultCellTextState is { FontSizeTouched: false })
            foreach (Row r in table.Rows)
            {
                foreach (Cell c in r.Cells)
                    foreach (var p in c.Paragraphs)
                        if (p is HtmlFragment hf
                            && System.Text.RegularExpressions.Regex.Match(hf.HtmlContent ?? "",
                                @"font-size\s*:\s*([\d.]+)\s*px") is { Success: true } fs
                            && double.TryParse(fs.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pxSize)
                            && pxSize > 0)
                        {
                            table.DefaultCellTextState.FontSize = (float)(pxSize * 0.75);
                            goto cssSizeDone;
                        }
            }
        cssSizeDone: ;
    }

    private static void ProcessTable(Page page, XmlNode tableNode, BindContext ctx)
    {
        var table = new Table { XmlGeneratorModel = true, XmlLineSpacing = ctx.Defaults.LineSpacing };
        ConfigureTable(table, tableNode, ctx);

        // Add to Paragraphs so it flows with preceding TextFragments and uses
        // BuildMultiPage — AddTable renders single-page only, so long tables get clipped.
        page.Paragraphs.Add(table);
    }

    private static void ProcessRow(Table table, XmlNode rowNode, BindContext ctx)
    {
        var row = table.Rows.Add();

        var bg = ParseColorValue(GetAttr(rowNode, "BackgroundColor"));
        if (bg is not null) row.BackgroundColor = bg;
        var minH = GetAttrLength(rowNode, "MinRowHeight");
        if (minH > 0) row.MinRowHeight = minH;
        var fixedH = GetAttrLength(rowNode, "FixedRowHeight");
        if (fixedH > 0) row.FixedRowHeight = fixedH;
        if (ParseVerticalAlignment(GetAttr(rowNode, "VerticalAlignment")) is { } rva)
            row.VerticalAlignment = rva;

        foreach (XmlNode child in rowNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Cell":
                    ProcessCell(row, child, ctx);
                    break;
                case "Border":
                    row.Border = ParseBorder(child);
                    break;
                case "DefaultCellBorder":
                    row.DefaultCellBorder = ParseBorder(child);
                    break;
                case "DefaultCellPadding":
                    row.DefaultCellPadding = ParseMargin(child);
                    break;
                case "DefaultCellTextState":
                    row.DefaultCellTextState = ParseTextState(child, row.DefaultCellTextState);
                    break;
            }
        }

        // Some templates (typically XSLT-produced) put a cell's text on its own line
        // with the stylesheet's indentation, e.g. "\n    *Gauge placeholder*\n  ". The
        // layout engine renders those leading/trailing blank lines as extra rows of
        // cell height, so the whole table row grows. Size the row to match: base
        // (one text line + padding) plus the max blank-line count across the row's cells,
        // each blank line at the cell line pitch. Rows whose cells carry no such
        // whitespace (the common case) are unaffected.
        var blank = MaxCellBlankLines(rowNode);
        if (blank > 0 && row.FixedRowHeight <= 0)
        {
            // Resolve the effective cell font size from the table's DefaultCellTextState
            // (its authored size). The row's auto-initialised DefaultCellTextState defaults
            // to 10 pt whether or not the XML set it, so it can't be trusted to override.
            var fs = table.DefaultCellTextState?.FontSize > 0 ? table.DefaultCellTextState!.FontSize : 10f;
            var pad = row.DefaultCellPadding ?? table.DefaultCellPadding;
            var padV = (pad?.Top ?? 0) + (pad?.Bottom ?? 0);
            // Each blank line adds one cell line pitch; the cell leading is
            // ≈ 1.14 × fontSize (vs the 1.2 the flow uses for body text).
            var pitch = fs * 1.14;
            row.MinRowHeight = Math.Max(row.MinRowHeight, fs + padV + blank * pitch);
        }
    }

    // Count the largest number of leading + trailing blank lines across a row's cell
    // texts (blank = a run separated by '\n' that holds only whitespace, before the first
    // / after the last non-blank line). A stylesheet-indented cell literal thus
    // renders as extra blank rows.
    private static int MaxCellBlankLines(XmlNode rowNode)
    {
        var max = 0;
        foreach (XmlNode cell in rowNode.ChildNodes)
        {
            if (cell.NodeType != XmlNodeType.Element || cell.LocalName != "Cell") continue;
            foreach (XmlNode frag in cell.ChildNodes)
            {
                if (frag.NodeType != XmlNodeType.Element || frag.LocalName != "TextFragment") continue;
                foreach (XmlNode seg in frag.ChildNodes)
                {
                    if (seg.NodeType != XmlNodeType.Element || seg.LocalName != "TextSegment") continue;
                    var raw = seg.InnerText ?? "";
                    if (raw.Trim().Length == 0) continue;
                    var lines = raw.Split('\n');
                    int lead = 0, trail = 0;
                    for (var i = 0; i < lines.Length && lines[i].Trim().Length == 0; i++) lead++;
                    for (var i = lines.Length - 1; i >= 0 && lines[i].Trim().Length == 0; i--) trail++;
                    max = Math.Max(max, lead + trail);
                }
            }
        }
        return max;
    }

    private static void ProcessCell(Row row, XmlNode cellNode, BindContext ctx)
    {
        var cell = row.Cells.Add();
        var colSpan = GetAttr(cellNode, "ColSpan");
        if (colSpan is not null && int.TryParse(colSpan, out var cs) && cs > 0)
            cell.ColSpan = cs;

        var bg = ParseColorValue(GetAttr(cellNode, "BackgroundColor"));
        if (bg is not null) cell.BackgroundColor = bg;
        if (ParseHAlign(GetAttr(cellNode, "Alignment")) is { } ca)
            cell.Alignment = ca;
        if (ParseVerticalAlignment(GetAttr(cellNode, "VerticalAlignment")) is { } cva)
            cell.VerticalAlignment = cva;

        foreach (XmlNode child in cellNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Border":
                    cell.Border = ParseBorder(child);
                    break;
                case "Margin":
                    cell.Margin = ParseMargin(child);
                    break;
                case "TextFragment":
                {
                    // A cell TextFragment carries its own styling (font/size/colour
                    // via a nested <TextState>) and a HorizontalAlignment attribute
                    // that drives the cell's text alignment. #$NL tokens split it
                    // into stacked one-line fragments (blank lines included).
                    foreach (var piece in BuildStyledTextFragments(child, ctx))
                        cell.Paragraphs.Add(piece);
                    if (ParseHAlign(GetAttr(child, "HorizontalAlignment")) is { } fa)
                        cell.Alignment = fa;
                    break;
                }
                case "HtmlFragment":
                {
                    // <HtmlFragment><HtmlContent>…</HtmlContent></HtmlFragment>
                    // appears in Aspose XML templates; pull the raw HTML so the
                    // Table layout path can paginate it along with text lines.
                    var htmlText = ExtractHtmlContent(child);
                    if (!string.IsNullOrEmpty(htmlText))
                        cell.Paragraphs.Add(new HtmlFragment(htmlText));
                    break;
                }
                case "Table":
                    cell.Paragraphs.Add(BuildTable(child, ctx));
                    break;
                case "Image":
                    cell.Paragraphs.Add(BuildImage(child, ctx));
                    break;
            }
        }
    }
}
