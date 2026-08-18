// Document.BindXml — Aspose.Pdf XML template binding
//
// Parses the Aspose.Pdf XML schema (namespace "Aspose.Pdf") and builds
// PDF pages with text, tables, and basic formatting.
//
// Supported elements:
//   Document, Page, PageInfo, Margin, DefaultTextState,
//   Table, Row, Cell, TextFragment, TextSegment, TextState,
//   Border, DefaultCellBorder, DefaultCellPadding

using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static class XmlBinding
{
    internal static void Bind(Document document, string xmlContent, string? baseDir = null)
    {
        var xdoc = new XmlDocument();
        // A non-XML or empty binding source must not blow up: BindXml tolerates it
        // and still yields a saveable (blank) document. Page.AsXml() is a stub that
        // returns "", so round-tripping a page through BindXml lands here.
        if (!string.IsNullOrWhiteSpace(xmlContent))
        {
            try { xdoc.LoadXml(xmlContent); }
            catch (System.Xml.XmlException) { xdoc = new XmlDocument(); }
        }

        var root = xdoc.DocumentElement;
        if (root is null)
        {
            // Guarantee at least one page so the resulting document saves cleanly.
            if (document.PageCount == 0) document.Pages.Add();
            return;
        }

        // Validate every Font attribute the XML references; an unresolved name
        // throws FontNotFoundException so XmlBinding-driven generation aborts
        // cleanly instead of silently dropping the styling. For example, an XML
        // that names a font we don't have ("Zurich Light BT") raises the
        // exception at BindXml time.
        ValidateFontReferences(root);

        var imageFiles = new List<string>();

        // The document-level <PageInfo><DefaultTextState LineSpacing=…> supplies the
        // inter-line leading used when laying out page text (per-paragraph the layout
        // engine reserves an empty leading line at font size + this leading).
        double docLineSpacing = 0;
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element && node.LocalName == "PageInfo")
                foreach (XmlNode ic in node.ChildNodes)
                    if (ic.NodeType == XmlNodeType.Element && ic.LocalName == "DefaultTextState")
                        docLineSpacing = GetAttrLength(ic, "LineSpacing", 0);
        }

        // Process <Page> elements. Auto-sequenced <Heading> numbering runs
        // document-wide, so the counters live for the whole bind.
        var headingCounters = new Dictionary<int, int>();
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            if (node.LocalName == "Page")
                ProcessPage(document, node, imageFiles, baseDir, docLineSpacing, headingCounters);
            // Skip PageInfo at document level (used for defaults)
        }

        // If no pages were added, add one
        if (document.PageCount == 0)
            document.Pages.Add();

        // Store deferred image file paths for validation during Save
        if (imageFiles.Count > 0)
            document.PendingXmlImageFiles = imageFiles;
    }

    /// <summary>Walk every element under <paramref name="root"/> and verify
    /// each Font attribute resolves through FontRepository. Standard 14 and
    /// known font-aliases never throw (the renderer always satisfies them);
    /// any other name unrecognised by FontRepository.FindFont raises
    /// FontNotFoundException keyed to the offending name.</summary>
    private static void ValidateFontReferences(XmlNode root)
    {
        Walk(root);
        static void Walk(XmlNode node)
        {
            if (node is XmlElement el)
            {
                var fontAttr = el.GetAttribute("Font");
                if (!string.IsNullOrEmpty(fontAttr) && !IsStandardOrAlias(fontAttr))
                {
                    var resolved = FontRepository.FindFont(fontAttr);
                    if (resolved is null)
                        throw new FontNotFoundException($"Font '{fontAttr}' was not found.");
                }
            }
            foreach (XmlNode child in node.ChildNodes)
                Walk(child);
        }
    }

    /// <summary>Standard-14 names + the well-known Acrobat /DR aliases never
    /// need disk-resolution — the renderer has built-in handling for them.</summary>
    private static bool IsStandardOrAlias(string name) => name switch
    {
        "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique"
            or "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic"
            or "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique"
            or "Symbol" or "ZapfDingbats"
            or "Helv" or "HeBo" or "HeOb" or "HeBO"
            or "TiRo" or "TiBo" or "TiIt" or "TiBI"
            or "Cour" or "CoBo" or "CoOb" or "CoBO"
            or "Symb" or "ZaDb" => true,
        _ => false,
    };

    private static void ProcessPage(Document document, XmlNode pageNode, List<string> imageFiles, string? baseDir, double docLineSpacing = 0, Dictionary<int, int>? headingCounters = null)
    {
        var page = document.Pages.Add();

        // Process child elements (including Header/Footer which may contain tables)
        foreach (XmlNode child in pageNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            ProcessPageChild(document, page, child, imageFiles, baseDir, docLineSpacing, headingCounters);
        }
    }

    // Dispatch one child of <Page> (or equivalently one child of a container such
    // as <FloatingBox> that participates in the page's flow). Kept as a separate
    // method so FloatingBox/FootNote flattening can reuse it.
    private static void ProcessPageChild(Document document, Page page, XmlNode child, List<string> imageFiles, string? baseDir, double docLineSpacing = 0, Dictionary<int, int>? headingCounters = null)
    {
        switch (child.LocalName)
        {
            case "PageInfo":
                ApplyPageInfo(page, child);
                break;
            case "Header":
            case "Footer":
            {
                // Build the page band: its TextFragments render on every page the
                // flow produces ($p/$P placeholders resolve at draw time).
                CollectImagesRecursive(child, imageFiles, baseDir);
                var band = new HeaderFooter();
                foreach (XmlNode hf in child.ChildNodes)
                {
                    if (hf.NodeType != XmlNodeType.Element) continue;
                    switch (hf.LocalName)
                    {
                        case "Margin":
                            band.Margin = ParseMargin(hf);
                            break;
                        case "TextFragment":
                        {
                            var bandText = ExtractTextFromFragment(hf);
                            if (!string.IsNullOrEmpty(bandText))
                                band.Paragraphs.Add(new TextFragment(bandText));
                            break;
                        }
                        case "Table":
                            band.Paragraphs.Add(BuildTable(hf, imageFiles, baseDir));
                            break;
                    }
                }
                if (child.LocalName == "Header") page.Header = band;
                else page.Footer = band;
                break;
            }
            case "Table":
                ProcessTable(page, child, imageFiles, baseDir);
                break;
            case "TextFragment":
                ProcessTextFragment(document, page, child, docLineSpacing);
                break;
            case "Image":
                CollectImageFile(child, imageFiles, baseDir);
                break;
            case "FloatingBox":
                ProcessFloatingBox(page, child, imageFiles, baseDir);
                break;
            case "Heading":
                // Headings here are styled paragraph text, not TOC entries. An
                // auto-sequenced heading prints its hierarchical section number
                // before the text ("1   Table of content"), counting per level
                // across the whole document; a level-N heading restarts the
                // deeper counters.
                var headingPrefix = string.Empty;
                if (headingCounters is not null
                    && string.Equals(GetAttr(child, "IsAutoSequence"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    var lvl = 1;
                    if (GetAttr(child, "Level") is { } lvlStr && int.TryParse(lvlStr, out var parsed) && parsed > 0)
                        lvl = parsed;
                    headingCounters[lvl] = (headingCounters.TryGetValue(lvl, out var c) ? c : 0) + 1;
                    foreach (var deeper in headingCounters.Keys.Where(k => k > lvl).ToList())
                        headingCounters.Remove(deeper);
                    var parts = new List<string>();
                    for (var k = 1; k <= lvl; k++)
                        if (headingCounters.TryGetValue(k, out var ck) && ck > 0)
                            parts.Add(ck.ToString(CultureInfo.InvariantCulture));
                    if (parts.Count > 0) headingPrefix = string.Join(".", parts) + "   ";
                }
                ProcessTextFragment(document, page, child, docLineSpacing, headingPrefix);
                break;
            case "FootNote":
                // Inline the footnote body as flow text; a true footnote pass
                // would anchor it at the page foot, but getting the text onto
                // *some* page is the correct baseline for pagination.
                ProcessTextFragment(document, page, child, docLineSpacing);
                break;
        }
    }

    // FloatingBox without explicit Left/Top gets inlined by the page's FlowLayout,
    // so emitting one with child paragraphs is enough for paginated flow. Absolute
    // boxes (Left/Top set) could be added here later; none of the regression XMLs
    // currently exercise that path.
    private static void ProcessFloatingBox(Page page, XmlNode boxNode, List<string> imageFiles, string? baseDir)
    {
        var fbox = new FloatingBox();
        foreach (XmlNode child in boxNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Margin":
                    fbox.Margin = ParseMargin(child);
                    break;
                case "TextFragment":
                case "Heading":
                case "FootNote":
                {
                    var text = ExtractTextFromFragment(child);
                    if (!string.IsNullOrEmpty(text))
                        fbox.Paragraphs.Add(new TextFragment(text));
                    break;
                }
                case "Table":
                {
                    var table = BuildTable(child, imageFiles, baseDir);
                    fbox.Paragraphs.Add(table);
                    break;
                }
                case "Image":
                    CollectImageFile(child, imageFiles, baseDir);
                    break;
                case "FloatingBox":
                    // Nested box — flatten its paragraphs into the outer box so
                    // FlowLayout only ever inlines one level of nesting.
                    FlattenNestedFloatingBox(fbox, child, imageFiles, baseDir);
                    break;
                // Graph / Line / All — visual primitives not yet renderable; ignore.
            }
        }
        page.Paragraphs.Add(fbox);
    }

    private static void FlattenNestedFloatingBox(FloatingBox outer, XmlNode innerNode,
        List<string> imageFiles, string? baseDir)
    {
        foreach (XmlNode child in innerNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "TextFragment":
                case "Heading":
                case "FootNote":
                {
                    var text = ExtractTextFromFragment(child);
                    if (!string.IsNullOrEmpty(text))
                        outer.Paragraphs.Add(new TextFragment(text));
                    break;
                }
                case "Table":
                    outer.Paragraphs.Add(BuildTable(child, imageFiles, baseDir));
                    break;
            }
        }
    }

    private static Table BuildTable(XmlNode tableNode, List<string> imageFiles, string? baseDir)
    {
        var table = new Table();
        ConfigureTable(table, tableNode, imageFiles, baseDir);
        return table;
    }

    // Shared table parsing: attributes (column widths, repeating header, column
    // adjustment, alignment) plus the table-level styling children (Border,
    // DefaultCellBorder, DefaultCellPadding) and rows. Used by both the page-level
    // ProcessTable and the nested BuildTable so styling is applied consistently.
    private static void ConfigureTable(Table table, XmlNode tableNode, List<string> imageFiles, string? baseDir)
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
        switch (GetAttr(tableNode, "Alignment"))
        {
            case "Center": table.Alignment = HorizontalAlignment.Center; break;
            case "Right": table.Alignment = HorizontalAlignment.Right; break;
        }

        foreach (XmlNode child in tableNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Row":
                    ProcessRow(table, child, imageFiles, baseDir);
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

    private static void ApplyPageInfo(Page page, XmlNode node)
    {
        var h = GetAttrLength(node, "Height");
        var w = GetAttrLength(node, "Width");
        if (h > 0 && w > 0)
            page.SetPageSize(w, h);

        // IsLandscape swaps the page dimensions so the wider side becomes the
        // width (the PageInfo semantics), which the generator paginator then
        // lays content across.
        if (string.Equals(GetAttr(node, "IsLandscape"), "true", StringComparison.OrdinalIgnoreCase))
            page.PageInfo.IsLandscape = true;

        // A nested <Margin> sets the page's content margins so the flow layout
        // insets the table/text rather than starting at the page edge.
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "Margin")
            {
                page.PageInfo.Margin = ParseMargin(child);
                break;
            }
        }
    }

    private static void ProcessTable(Page page, XmlNode tableNode, List<string> imageFiles, string? baseDir)
    {
        var table = new Table();
        ConfigureTable(table, tableNode, imageFiles, baseDir);

        // Add to Paragraphs so it flows with preceding TextFragments and uses
        // BuildMultiPage — AddTable renders single-page only, so long tables get clipped.
        page.Paragraphs.Add(table);
    }

    private static void ProcessRow(Table table, XmlNode rowNode, List<string> imageFiles, string? baseDir)
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
                    ProcessCell(row, child, imageFiles, baseDir);
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

    private static void ProcessCell(Row row, XmlNode cellNode, List<string> imageFiles, string? baseDir)
    {
        var cell = row.Cells.Add();
        var colSpan = GetAttr(cellNode, "ColSpan");
        if (colSpan is not null && int.TryParse(colSpan, out var cs) && cs > 0)
            cell.ColSpan = cs;

        var bg = ParseColorValue(GetAttr(cellNode, "BackgroundColor"));
        if (bg is not null) cell.BackgroundColor = bg;
        switch (GetAttr(cellNode, "Alignment"))
        {
            case "Center": cell.Alignment = HorizontalAlignment.Center; break;
            case "Right": cell.Alignment = HorizontalAlignment.Right; break;
        }
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
                case "TextFragment":
                {
                    // A cell TextFragment carries its own styling (font/size/colour
                    // via a nested <TextState>) and a HorizontalAlignment attribute
                    // that drives the cell's text alignment.
                    var frag = BuildStyledTextFragment(child);
                    if (frag is not null)
                        cell.Paragraphs.Add(frag);
                    switch (GetAttr(child, "HorizontalAlignment"))
                    {
                        case "Center": cell.Alignment = HorizontalAlignment.Center; break;
                        case "Right": cell.Alignment = HorizontalAlignment.Right; break;
                    }
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
                    cell.Paragraphs.Add(BuildTable(child, imageFiles, baseDir));
                    break;
                case "Image":
                    CollectImageFile(child, imageFiles, baseDir);
                    break;
            }
        }
    }

    private static void CollectImageFile(XmlNode imageNode, List<string> imageFiles, string? baseDir)
    {
        var file = GetAttr(imageNode, "File");
        if (file is not null)
            imageFiles.Add(file);
    }

    private static void CollectImagesRecursive(XmlNode node, List<string> imageFiles, string? baseDir)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "Image")
                CollectImageFile(child, imageFiles, baseDir);
            else
                CollectImagesRecursive(child, imageFiles, baseDir);
        }
    }

    private static void ProcessTextFragment(Document document, Page page, XmlNode fragNode, double docLineSpacing = 0, string textPrefix = "")
    {
        var text = ExtractTextFromFragment(fragNode);
        if (!string.IsNullOrEmpty(text)) text = textPrefix + text;
        var id = GetAttr(fragNode, "id");
        if (string.IsNullOrEmpty(text))
        {
            // A text-less fragment produces no layout, but an id-carrying one must
            // still be resolvable through Document.GetObjectById.
            if (id is not null)
                document.RegisterXmlObject(id, new TextFragment(string.Empty) { Id = id });
            return;
        }

        // Aspose's generator default body size is 10 pt (the document-level
        // <DefaultTextState> is a descriptor, not applied to these fragments).
        double fontSize = 10;
        string? fontName = null;
        Color? fg = null;
        MarginInfo? margin = null;

        // Fragment-level <TextState> (font/size/colour) and <Margin> (paragraph
        // spacing). A per-<TextSegment> <TextState FontSize=…> overrides the size
        // (e.g. the title's 20 pt sits on the segment, not the fragment).
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "TextState":
                    if (GetAttr(child, "FontSize") is not null) fontSize = GetAttrLength(child, "FontSize", fontSize);
                    fontName ??= GetAttr(child, "Font");
                    fg ??= ParseColorValue(GetAttr(child, "ForegroundColor"));
                    break;
                case "Margin":
                    margin = ParseMargin(child);
                    break;
                case "TextSegment":
                    foreach (XmlNode sc in child.ChildNodes)
                    {
                        if (sc.NodeType == XmlNodeType.Element && sc.LocalName == "TextState")
                        {
                            if (GetAttr(sc, "FontSize") is not null) fontSize = GetAttrLength(sc, "FontSize", fontSize);
                            fontName ??= GetAttr(sc, "Font");
                            fg ??= ParseColorValue(GetAttr(sc, "ForegroundColor"));
                        }
                    }
                    break;
            }
        }

        // The layout engine reserves an empty leading line per paragraph (the implicit
        // empty segment the parameterless TextFragment ctor creates) at the paragraph's
        // line height, on top of the paragraph's top margin. Reserve the same space before
        // the text so multi-paragraph flow occupies the same vertical extent — the total
        // text height is what decides where a following table paginates. Two line heights
        // (empty + text seating) at (fontSize + docLineSpacing) give the per-paragraph
        // advance to within the pagination tolerance.
        // Per-paragraph vertical reservation calibrated to the layout engine's
        // per-paragraph advance (an empty leading line plus text seating): 1.85 line
        // heights at the paragraph font size, plus the document line spacing.
        var leadReserve = 1.85 * fontSize + docLineSpacing;
        var mTop = (margin?.Top ?? 0) + leadReserve;

        var tf = new TextFragment(text);
        if (id is not null)
        {
            tf.Id = id;
            document.RegisterXmlObject(id, tf);
        }
        tf.Margin = new MarginInfo { Top = mTop };
        tf.TextState.FontSize = (float)fontSize;
        if (fontName is not null)
            tf.TextState.FontName = fontName;
        if (fg is not null)
            tf.TextState.ForegroundColor = fg;

        page.Paragraphs.Add(tf);
    }

    // Build a TextFragment for a cell from its <TextFragment> node, applying a
    // nested <TextState> (font / size / colour) and any per-segment font size.
    // Returns null when the fragment carries no text (an empty header cell).
    private static TextFragment? BuildStyledTextFragment(XmlNode fragNode)
    {
        var text = ExtractTextFromFragment(fragNode);
        if (string.IsNullOrEmpty(text)) return null;
        var tf = new TextFragment(text);
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "TextState")
            {
                ApplyTextState(child, tf.TextState);
                break;
            }
        }
        return tf;
    }

    // Parse a <TextState>/<DefaultCellTextState> element into a new TextState,
    // seeded from an existing one so unspecified properties are preserved.
    private static TextState ParseTextState(XmlNode node, TextState? seed)
    {
        var ts = seed ?? new TextState();
        ApplyTextState(node, ts);
        return ts;
    }

    private static void ApplyTextState(XmlNode node, TextState ts)
    {
        var font = GetAttr(node, "Font");
        if (!string.IsNullOrEmpty(font))
        {
            ts.FontName = font;
            if (font.Contains("Bold", StringComparison.OrdinalIgnoreCase)) ts.IsBold = true;
        }
        var size = GetAttrLength(node, "FontSize");
        if (size > 0) ts.FontSize = (float)size;
        var fg = ParseColorValue(GetAttr(node, "ForegroundColor"));
        if (fg is not null) ts.ForegroundColor = fg;
        var ls = GetAttrLength(node, "LineSpacing", -1);
        if (ls >= 0) ts.LineSpacing = (float)ls;
    }

    private static VerticalAlignment? ParseVerticalAlignment(string? value) => value switch
    {
        "Center" => VerticalAlignment.Center,
        "Bottom" => VerticalAlignment.Bottom,
        "Top" => VerticalAlignment.Top,
        _ => null,
    };

    private static string ExtractTextFromFragment(XmlNode fragNode)
    {
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextSegment")
            {
                // Get text content (non-element text nodes)
                foreach (XmlNode textChild in child.ChildNodes)
                {
                    if (textChild.NodeType == XmlNodeType.Text ||
                        textChild.NodeType == XmlNodeType.CDATA)
                        sb.Append(DecodeXmlEntities(textChild.Value ?? ""));
                }
            }
        }
        // Normalise display whitespace the way the layout engine does: an
        // XSLT-produced literal (e.g. "\n   *Gauge placeholder*\n ") carries the
        // stylesheet's indentation, which would otherwise render as a leading
        // blank line / trailing space line and knock the cell text out of
        // vertical alignment with its neighbours. Collapse runs to one space.
        // Text authored WITHOUT line structure keeps its spacing verbatim — a
        // segment's deliberate trailing space ("Table of content ") survives.
        var raw = sb.ToString();
        if (raw.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0) return raw;
        return System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
    }

    // The Aspose XML schema stores an HtmlFragment's body two ways: as a
    // direct text/CDATA child of <HtmlFragment>, or wrapped in a nested
    // <HtmlContent>…</HtmlContent> element. Either shape is valid input;
    // we flatten both into a single string that the Table/HtmlFragment
    // rendering path can strip via HtmlFragment.StripHtmlTags.
    private static string ExtractHtmlContent(XmlNode fragNode)
    {
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Text || child.NodeType == XmlNodeType.CDATA)
            {
                sb.Append(DecodeXmlEntities(child.Value ?? ""));
            }
            else if (child.NodeType == XmlNodeType.Element && child.LocalName == "HtmlContent")
            {
                foreach (XmlNode tc in child.ChildNodes)
                {
                    if (tc.NodeType == XmlNodeType.Text || tc.NodeType == XmlNodeType.CDATA)
                        sb.Append(DecodeXmlEntities(tc.Value ?? ""));
                }
            }
        }
        return sb.ToString();
    }

    private static string DecodeXmlEntities(string text)
    {
        // Decode Aspose-style XML character references: _x0027_ → '
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"_x([0-9A-Fa-f]{4})_",
            m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());
    }

    private static MarginInfo ParseMargin(XmlNode node)
    {
        return new MarginInfo
        {
            Left = GetAttrLength(node, "Left"),
            Right = GetAttrLength(node, "Right"),
            Top = GetAttrLength(node, "Top"),
            Bottom = GetAttrLength(node, "Bottom"),
        };
    }

    // Parse a <Border>/<DefaultCellBorder> element. Its children name the sides
    // (All/Box or Top/Bottom/Left/Right), each carrying a LineWidth and optional
    // Color. The result feeds BorderInfo.Side (which sides to draw) plus per-side
    // widths when the sides differ.
    private static BorderInfo ParseBorder(XmlNode node)
    {
        var border = new BorderInfo { Side = BorderSide.None };
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var width = GetAttrLength(child, "LineWidth", 1);
            var color = ParseColorValue(GetAttr(child, "Color"));
            switch (child.LocalName)
            {
                case "All":
                case "Box":
                    border.Side |= BorderSide.Box;
                    border.Width = width;
                    if (color is not null) border.Color = color;
                    break;
                case "Top":
                    border.Side |= BorderSide.Top;
                    border.Width = width;
                    border.Top = new GraphInfo { LineWidth = (float)width, Color = color };
                    if (color is not null) border.Color = color;
                    break;
                case "Bottom":
                    border.Side |= BorderSide.Bottom;
                    border.Width = width;
                    border.Bottom = new GraphInfo { LineWidth = (float)width, Color = color };
                    if (color is not null) border.Color = color;
                    break;
                case "Left":
                    border.Side |= BorderSide.Left;
                    border.Width = width;
                    border.Left = new GraphInfo { LineWidth = (float)width, Color = color };
                    if (color is not null) border.Color = color;
                    break;
                case "Right":
                    border.Side |= BorderSide.Right;
                    border.Width = width;
                    border.Right = new GraphInfo { LineWidth = (float)width, Color = color };
                    if (color is not null) border.Color = color;
                    break;
            }
        }
        return border;
    }

    // Parse a colour attribute: a #rrggbb / #aarrggbb hex string, or a named
    // colour (Aspose XML also allows names like "Black"/"Gray"). Returns null
    // when the attribute is absent so callers leave the property unset.
    private static Color? ParseColorValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith('#'))
            return Color.Parse(value);
        try
        {
            var named = System.Drawing.Color.FromName(value);
            if (named.A != 0 || string.Equals(value, "Transparent", StringComparison.OrdinalIgnoreCase))
                return Color.FromRgb(named.R, named.G, named.B);
        }
        catch { /* fall through */ }
        return null;
    }

    private static string? GetAttr(XmlNode node, string name)
        => node.Attributes?[name]?.Value;

    private static double GetAttrDouble(XmlNode node, string name, double defaultValue = 0)
    {
        var val = GetAttr(node, name);
        return val is not null && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : defaultValue;
    }

    // Length attribute parser that honours Aspose XML unit suffixes
    // (cm/mm/in/pt/px). A bare number is points. Used for page/table dimensions
    // and margins so e.g. Top="2.6cm" converts to 73.7 pt rather than parsing
    // to 0 (double.TryParse fails on the suffix).
    private static double GetAttrLength(XmlNode node, string name, double defaultValue = 0)
        => ParseLength(GetAttr(node, name), defaultValue);

    private static double ParseLength(string? val, double defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(val)) return defaultValue;
        val = val.Trim();
        double factor = 1.0; // points
        string num = val;
        // Longest suffix first: "inch" must match before its "in" prefix would.
        foreach (var (suffix, f) in new[] { ("inch", 72.0), ("cm", 72.0 / 2.54), ("mm", 72.0 / 25.4), ("in", 72.0), ("pt", 1.0), ("px", 1.0) })
        {
            if (val.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                factor = f;
                num = val[..^suffix.Length].Trim();
                break;
            }
        }
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d * factor
            : defaultValue;
    }
}
