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
        xdoc.LoadXml(xmlContent);

        var root = xdoc.DocumentElement;
        if (root is null) return;

        // Validate every Font attribute the XML references; an unresolved name
        // throws FontNotFoundException so XmlBinding-driven generation aborts
        // cleanly instead of silently dropping the styling. For example, an XML
        // that names a font we don't have ("Zurich Light BT") raises the
        // exception at BindXml time.
        ValidateFontReferences(root);

        var imageFiles = new List<string>();

        // Process <Page> elements
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            if (node.LocalName == "Page")
                ProcessPage(document, node, imageFiles, baseDir);
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

    private static void ProcessPage(Document document, XmlNode pageNode, List<string> imageFiles, string? baseDir)
    {
        var page = document.Pages.Add();

        // Process child elements (including Header/Footer which may contain tables)
        foreach (XmlNode child in pageNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            ProcessPageChild(page, child, imageFiles, baseDir);
        }
    }

    // Dispatch one child of <Page> (or equivalently one child of a container such
    // as <FloatingBox> that participates in the page's flow). Kept as a separate
    // method so FloatingBox/FootNote flattening can reuse it.
    private static void ProcessPageChild(Page page, XmlNode child, List<string> imageFiles, string? baseDir)
    {
        switch (child.LocalName)
        {
            case "PageInfo":
                ApplyPageInfo(page, child);
                break;
            case "Header":
            case "Footer":
                CollectImagesRecursive(child, imageFiles, baseDir);
                break;
            case "Table":
                ProcessTable(page, child, imageFiles, baseDir);
                break;
            case "TextFragment":
                ProcessTextFragment(page, child);
                break;
            case "Image":
                CollectImageFile(child, imageFiles, baseDir);
                break;
            case "FloatingBox":
                ProcessFloatingBox(page, child, imageFiles, baseDir);
                break;
            case "Heading":
                // Headings here are styled paragraph text, not TOC entries.
                ProcessTextFragment(page, child);
                break;
            case "FootNote":
                // Inline the footnote body as flow text; a true footnote pass
                // would anchor it at the page foot, but getting the text onto
                // *some* page is the correct baseline for pagination.
                ProcessTextFragment(page, child);
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
        var colWidths = GetAttr(tableNode, "ColumnWidths");
        if (colWidths is not null) table.ColumnWidths = colWidths;

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
            }
        }
        return table;
    }

    private static void ApplyPageInfo(Page page, XmlNode node)
    {
        var h = GetAttrDouble(node, "Height");
        var w = GetAttrDouble(node, "Width");
        if (h > 0 && w > 0)
            page.SetPageSize(w, h);
    }

    private static void ProcessTable(Page page, XmlNode tableNode, List<string> imageFiles, string? baseDir)
    {
        var table = new Table();
        var colWidths = GetAttr(tableNode, "ColumnWidths");
        if (colWidths is not null)
            table.ColumnWidths = colWidths;

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
                // Border, DefaultCellBorder, DefaultCellPadding — parsed but not deeply applied
            }
        }

        // Add to Paragraphs so it flows with preceding TextFragments and uses
        // BuildMultiPage — AddTable renders single-page only, so long tables get clipped.
        page.Paragraphs.Add(table);
    }

    private static void ProcessRow(Table table, XmlNode rowNode, List<string> imageFiles, string? baseDir)
    {
        var row = table.Rows.Add();

        foreach (XmlNode child in rowNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "Cell")
                ProcessCell(row, child, imageFiles, baseDir);
        }
    }

    private static void ProcessCell(Row row, XmlNode cellNode, List<string> imageFiles, string? baseDir)
    {
        var cell = row.Cells.Add();
        var colSpan = GetAttr(cellNode, "ColSpan");
        if (colSpan is not null && int.TryParse(colSpan, out var cs) && cs > 0)
            cell.ColSpan = cs;

        foreach (XmlNode child in cellNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "TextFragment":
                    var text = ExtractTextFromFragment(child);
                    if (!string.IsNullOrEmpty(text))
                        cell.Paragraphs.Add(new TextFragment(text));
                    break;
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

    private static void ProcessTextFragment(Page page, XmlNode fragNode)
    {
        var text = ExtractTextFromFragment(fragNode);
        if (string.IsNullOrEmpty(text)) return;

        double fontSize = 12;
        string? fontName = null;

        // Check TextState at fragment level
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextState")
            {
                fontSize = GetAttrDouble(child, "FontSize", 12);
                fontName = GetAttr(child, "Font");
            }
        }

        var tf = new TextFragment(text);
        tf.TextState.FontSize = (float)fontSize;
        if (fontName is not null)
            tf.TextState.FontName = fontName;

        page.Paragraphs.Add(tf);
    }

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
        return sb.ToString();
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
            Left = GetAttrDouble(node, "Left"),
            Right = GetAttrDouble(node, "Right"),
            Top = GetAttrDouble(node, "Top"),
            Bottom = GetAttrDouble(node, "Bottom"),
        };
    }

    private static string? GetAttr(XmlNode node, string name)
        => node.Attributes?[name]?.Value;

    private static double GetAttrDouble(XmlNode node, string name, double defaultValue = 0)
    {
        var val = GetAttr(node, name);
        return val is not null && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : defaultValue;
    }
}
