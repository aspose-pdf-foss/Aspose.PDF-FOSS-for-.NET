// Document.BindXml — the round-trip dialect
//
// Reads back what Helpers.XmlSerialization wrote: a literal description of a
// generator DOM, element for object and attribute for property. The rest of this
// binder (XmlBinding.cs) reads templates a PERSON authored, where the same element
// names carry era calibrations — a table laid out in the XML-generator dialect,
// a floating box positioned relative to the content area, cell text with its
// stylesheet indentation collapsed. Those exist to recover intent from markup; a
// document that already exists has no intent left to recover, so this path rebuilds
// the objects exactly as they were written and applies none of them.

using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static partial class XmlBinding
{
    private static void BindRoundTrip(Document document, XmlNode root)
    {
        var images = new List<Image>();
        // The document's own page margins come FIRST: layout falls back to them per side
        // for a page that declared none, so they must be in place before the pages are.
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element || node.LocalName != "DocumentPageInfo") continue;
            if (FirstElementChild(node, "Margin") is { } docMargin)
            {
                // Only the sides the attribute set names were authored; assigning the
                // others would touch them at zero (see the writer).
                var m = document.PageInfo.Margin;
                if (GetAttr(docMargin, "Left") is { Length: > 0 }) m.Left = GetAttrLength(docMargin, "Left");
                if (GetAttr(docMargin, "Right") is { Length: > 0 }) m.Right = GetAttrLength(docMargin, "Right");
                if (GetAttr(docMargin, "Top") is { Length: > 0 }) m.Top = GetAttrLength(docMargin, "Top");
                if (GetAttr(docMargin, "Bottom") is { Length: > 0 }) m.Bottom = GetAttrLength(docMargin, "Bottom");
            }
        }
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element || node.LocalName != "Page") continue;
            BuildRoundTripPage(document, node, images);
        }
        if (document.PageCount == 0) document.Pages.Add();
        // Image File paths are validated at Save time, against their CURRENT values.
        if (images.Count > 0) document.PendingXmlImages = images;
    }

    private static void BuildRoundTripPage(Document document, XmlNode pageNode, List<Image> images)
    {
        var page = document.Pages.Add();
        foreach (XmlNode child in pageNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "PageInfo":
                    ApplyRoundTripPageInfo(page, child);
                    break;
                case "Header":
                    page.Header = BuildRoundTripBand(child, images);
                    break;
                case "Footer":
                    page.Footer = BuildRoundTripBand(child, images);
                    break;
                case "BackgroundArtifact":
                    if (FirstElementChild(child, "Data") is { } bgData
                        && bgData.InnerText is { Length: > 0 } bgBase64)
                        page.Artifacts.Add(new BackgroundArtifact
                        {
                            BackgroundImage = new MemoryStream(Convert.FromBase64String(bgBase64)),
                        });
                    break;
                default:
                    if (BuildRoundTripParagraph(child, images) is { } paragraph)
                        page.Paragraphs.Add(paragraph);
                    break;
            }
        }
    }

    private static void ApplyRoundTripPageInfo(Page page, XmlNode node)
    {
        var w = GetAttrLength(node, "Width");
        var h = GetAttrLength(node, "Height");
        if (w > 0 && h > 0) page.SetPageSize(w, h);
        if (FirstElementChild(node, "Margin") is { } margin)
            page.PageInfo.Margin = ParseMargin(margin);
    }

    private static HeaderFooter BuildRoundTripBand(XmlNode node, List<Image> images)
    {
        var band = new HeaderFooter();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "Margin") { band.Margin = ParseMargin(child); continue; }
            if (BuildRoundTripParagraph(child, images) is { } paragraph)
                band.Paragraphs.Add(paragraph);
        }
        return band;
    }

    /// <summary>One paragraph-level object, or null when the element names none.</summary>
    private static BaseParagraph? BuildRoundTripParagraph(XmlNode node, List<Image> images)
    {
        switch (node.LocalName)
        {
            case "TextFragment": return BuildRoundTripFragment(node);
            case "Heading": return BuildRoundTripHeading(node);
            case "Table": return BuildRoundTripTable(node, images);
            case "FloatingBox": return BuildRoundTripFloatingBox(node, images);
            case "Image": return BuildRoundTripImage(node, images);
            case "Graph": return BuildGraph(node);
            case "HtmlFragment":
            {
                var html = new HtmlFragment(ExtractHtmlContent(node));
                ApplyRoundTripParagraph(html, node);
                ApplyRoundTripMargin(node, m => html.Margin = m);
                // The shadowing set (see ApplyRoundTripParagraph).
                html.IsInNewPage = Flag(node, "IsInNewPage");
                html.IsKeptWithNext = Flag(node, "IsKeptWithNext");
                return html;
            }
            default: return null;
        }
    }

    /// <summary>The attributes every paragraph shares.</summary>
    /// <remarks>⚠ Several of these are SHADOWED with <c>new</c> properties on the
    /// concrete types — Margin on Table/FloatingBox/Image/HtmlFragment/TextFragment,
    /// alignment and the inline/new-page flags on TextFragment, alignment and ZIndex on
    /// FloatingBox — and layout reads the shadowing one. Assigning through a base-typed
    /// reference fills a property nothing looks at, so the shadowed ones are applied per
    /// type below and Margin through <see cref="ApplyRoundTripMargin"/>.</remarks>
    private static void ApplyRoundTripParagraph(BaseParagraph p, XmlNode node)
    {
        ApplyParagraphAttributes(p, node);
        if (string.Equals(GetAttr(node, "IsFirstParagraphInColumn"), "true", StringComparison.OrdinalIgnoreCase))
            p.IsFirstParagraphInColumn = true;
        p.Hyperlink = ParseRoundTripHyperlink(node) ?? p.Hyperlink;
    }

    private static bool Flag(XmlNode node, string name)
        => string.Equals(GetAttr(node, name), "true", StringComparison.OrdinalIgnoreCase);

    private static void ApplyRoundTripMargin(XmlNode node, Action<MarginInfo> set)
    {
        if (FirstElementChild(node, "Margin") is { } margin) set(ParseMargin(margin));
    }

    private static Hyperlink? ParseRoundTripHyperlink(XmlNode node)
    {
        if (GetAttr(node, "HyperlinkUrl") is { Length: > 0 } url) return new WebHyperlink(url);
        if (GetAttr(node, "HyperlinkFile") is { Length: > 0 } file) return new FileHyperlink(file);
        if (GetAttr(node, "HyperlinkPage") is { } page && int.TryParse(page, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var pageNumber) && pageNumber > 0)
            return new LocalHyperlink { TargetPageNumber = pageNumber };
        return null;
    }

    private static Heading BuildRoundTripHeading(XmlNode node)
    {
        var level = GetAttr(node, "Level") is { } lvl && int.TryParse(lvl, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
        var heading = new Heading(level);
        ApplyRoundTripParagraph(heading, node);
        ApplyRoundTripMargin(node, m => heading.Margin = m);
        if (GetAttr(node, "Style") is { } style && Enum.TryParse<NumberingStyle>(style, out var numbering))
            heading.Style = numbering;
        if (string.Equals(GetAttr(node, "IsAutoSequence"), "true", StringComparison.OrdinalIgnoreCase))
            heading.IsAutoSequence = true;
        if (string.Equals(GetAttr(node, "IsInList"), "true", StringComparison.OrdinalIgnoreCase))
            heading.IsInList = true;
        foreach (var (segment, state, isFragmentLevel) in RoundTripStates(node))
        {
            if (isFragmentLevel) ApplyRoundTripTextState(state, heading.TextState);
            else heading.Segments.Add(segment!);
        }
        return heading;
    }

    /// <summary>The state elements under a fragment-shaped node, in document order: the
    /// bare one carries the object's own style, each wrapping one carries a segment.
    /// </summary>
    private static IEnumerable<(TextSegment? Segment, XmlNode State, bool IsFragmentLevel)>
        RoundTripStates(XmlNode node)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "TextState") continue;
            if (FirstElementChild(child, "TextSegment") is { } wrapped)
            {
                var segment = new TextSegment(SegmentText(wrapped));
                ApplyRoundTripTextState(child, segment.TextState);
                segment.Hyperlink = ParseRoundTripHyperlink(child);
                yield return (segment, child, false);
            }
            else
            {
                yield return (null, child, true);
            }
        }
    }

    // ---- text ------------------------------------------------------------------

    /// <summary>A written fragment carries its own bare <c>&lt;TextState&gt;</c> followed
    /// by one state element per segment, each WRAPPING its <c>&lt;TextSegment&gt;</c>.
    /// Text is taken verbatim — the whitespace is the DOM's, not a stylesheet's.</summary>
    private static TextFragment BuildRoundTripFragment(XmlNode node)
    {
        var tf = new TextFragment(GetAttr(node, "Text") ?? string.Empty);
        // The ctor seeds one segment for the text it was given; the written segments are
        // the fragment's WHOLE list, seeded one included, so keeping the ctor's would
        // insert a duplicate on every round-trip.
        tf.Segments.Clear();
        ApplyRoundTripParagraph(tf, node);
        ApplyRoundTripMargin(node, m => tf.Margin = m);
        // The shadowing set (see ApplyRoundTripParagraph): without these the fragment
        // never joins its neighbours and every inline run starts its own line.
        if (ParseHAlign(GetAttr(node, "HorizontalAlignment")) is { } tfHa) tf.HorizontalAlignment = tfHa;
        if (ParseVerticalAlignment(GetAttr(node, "VerticalAlignment")) is { } tfVa) tf.VerticalAlignment = tfVa;
        tf.IsInLineParagraph = Flag(node, "IsInLineParagraph");
        tf.IsInNewPage = Flag(node, "IsInNewPage");
        if (ParseRoundTripHyperlink(node) is { } tfLink) tf.Hyperlink = tfLink;
        if (Flag(node, "AutoNoteText")) tf.AutoNoteText = true;
        foreach (var (segment, state, isFragmentLevel) in RoundTripStates(node))
        {
            if (isFragmentLevel) ApplyRoundTripTextState(state, tf.TextState);
            else tf.Segments.Add(segment!);
        }
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "FootNote") tf.FootNote = BuildRoundTripNote(child);
            else if (child.LocalName == "EndNote") tf.EndNote = BuildRoundTripNote(child);
        }
        return tf;
    }

    private static Note BuildRoundTripNote(XmlNode node)
    {
        var note = new Note();
        if (GetAttr(node, "Text") is { Length: > 0 } marker) note.Text = marker;
        var images = new List<Image>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextState" && FirstElementChild(child, "TextSegment") is null)
            {
                ApplyRoundTripTextState(child, note.TextState);
                continue;
            }
            if (BuildRoundTripParagraph(child, images) is { } paragraph)
                note.Paragraphs.Add(paragraph);
        }
        return note;
    }

    private static string SegmentText(XmlNode segNode)
    {
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode child in segNode.ChildNodes)
        {
            if (child.NodeType is XmlNodeType.Text or XmlNodeType.CDATA
                or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                sb.Append(child.Value ?? string.Empty);
        }
        return sb.ToString();
    }

    /// <summary>Every property the writer emits, read straight back. Unlike the
    /// authored-template reader this neither infers bold from the font name nor
    /// re-resolves the name through the system repository: the writer put out exactly
    /// what the state held.</summary>
    private static void ApplyRoundTripTextState(XmlNode node, TextState state)
    {
        if (GetAttr(node, "FontSize") is not null)
            state.FontSize = (float)GetAttrLength(node, "FontSize", state.FontSize);
        // Resolve the name back to a FACE, not just a name. The DOM this describes held
        // a Font object (FontRepository.FindFont), and the layout measures through it —
        // leaving the name over the default Helvetica re-measures the text against the
        // wrong widths and moves the line breaks.
        if (GetAttr(node, "Font") is { Length: > 0 } font)
        {
            if (FontRepository.TryFindFont(font) is { } face) state.Font = face;
            else state.FontName = font;
        }
        if (ParseColorValue(GetAttr(node, "ForegroundColor")) is { } fg) state.ForegroundColor = fg;
        if (ParseColorValue(GetAttr(node, "StrokingColor")) is { } stroking) state.StrokingColor = stroking;
        if (GetAttr(node, "FontStyle") is { } fs && int.TryParse(fs, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var flags))
        {
            if ((flags & 1) != 0) state.IsBold = true;
            if ((flags & 2) != 0) state.IsItalic = true;
        }
        if (string.Equals(GetAttr(node, "Underline"), "true", StringComparison.OrdinalIgnoreCase))
            state.Underline = true;
        if (string.Equals(GetAttr(node, "IsSuperscript"), "true", StringComparison.OrdinalIgnoreCase))
            state.IsSuperscript = true;
        if (string.Equals(GetAttr(node, "IsSubscript"), "true", StringComparison.OrdinalIgnoreCase))
            state.IsSubscript = true;
        if (GetAttr(node, "LineSpacing") is not null)
            state.LineSpacing = (float)GetAttrLength(node, "LineSpacing");
        if (ParseHAlign(GetAttr(node, "TextHorizontalAlignment")) is { } tsHa)
            state.HorizontalAlignment = tsHa;
        if (GetAttr(node, "CharacterSpacing") is not null)
            state.CharacterSpacing = (float)GetAttrDouble(node, "CharacterSpacing");
        if (GetAttr(node, "WordSpacing") is not null)
            state.WordSpacing = (float)GetAttrDouble(node, "WordSpacing");
        if (GetAttr(node, "HorizontalScaling") is not null)
            state.HorizontalScaling = (float)GetAttrDouble(node, "HorizontalScaling", 100);
        if (GetAttr(node, "Rotation") is not null)
            state.Rotation = GetAttrDouble(node, "Rotation");
        if (GetAttr(node, "TextRise") is not null)
            state.TextRise = GetAttrDouble(node, "TextRise");
    }

    // ---- tables ----------------------------------------------------------------

    private static Table BuildRoundTripTable(XmlNode node, List<Image> images)
    {
        var table = new Table();
        ApplyRoundTripParagraph(table, node);
        ApplyRoundTripMargin(node, m => table.Margin = m);
        if (GetAttr(node, "ColumnWidths") is { } widths) table.ColumnWidths = widths;
        if (GetAttr(node, "RepeatingRowsCount") is { } rrc && int.TryParse(rrc, out var rows))
            table.RepeatingRowsCount = rows;
        if (GetAttr(node, "RepeatingColumnsCount") is { } rcc && int.TryParse(rcc, out var cols))
            table.RepeatingColumnsCount = cols;
        if (GetAttr(node, "Broken") is { } broken && Enum.TryParse<TableBroken>(broken, out var brokenMode))
            table.Broken = brokenMode;
        if (string.Equals(GetAttr(node, "IsBroken"), "false", StringComparison.OrdinalIgnoreCase))
            table.IsBroken = false;
        if (string.Equals(GetAttr(node, "IsBordersIncluded"), "true", StringComparison.OrdinalIgnoreCase))
            table.IsBordersIncluded = true;
        if (GetAttr(node, "CornerStyle") is { } corner && Enum.TryParse<BorderCornerStyle>(corner, out var cornerStyle))
            table.CornerStyle = cornerStyle;
        if (GetAttr(node, "DefaultColumnWidth") is { } dcw) table.DefaultColumnWidth = dcw;
        if (GetAttr(node, "Left") is not null) table.Left = (float)GetAttrLength(node, "Left");
        if (GetAttr(node, "Top") is not null) table.Top = (float)GetAttrLength(node, "Top");
        table.ColumnAdjustment = GetAttr(node, "ColumnAdjustment") switch
        {
            "AutoFitToWindow" => ColumnAdjustment.AutoFitToWindow,
            "AutoFitToContent" => ColumnAdjustment.AutoFitToContent,
            _ => ColumnAdjustment.Customized,
        };
        if (ParseHAlign(GetAttr(node, "Alignment")) is { } alignment) table.Alignment = alignment;
        if (ParseColorValue(GetAttr(node, "BackgroundColor")) is { } bg) table.BackgroundColor = bg;

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Border": table.Border = ParseBorder(child); break;
                case "DefaultCellBorder": table.DefaultCellBorder = ParseBorder(child); break;
                case "DefaultCellPadding": table.DefaultCellPadding = ParseMargin(child); break;
                case "DefaultCellTextState":
                    ApplyRoundTripTextState(child, table.DefaultCellTextState ??= new TextState());
                    break;
                case "Row": BuildRoundTripRow(table, child, images); break;
            }
        }
        return table;
    }

    private static void BuildRoundTripRow(Table table, XmlNode node, List<Image> images)
    {
        var row = table.Rows.Add();
        if (ParseColorValue(GetAttr(node, "BackgroundColor")) is { } bg) row.BackgroundColor = bg;
        if (GetAttr(node, "MinRowHeight") is not null) row.MinRowHeight = GetAttrLength(node, "MinRowHeight");
        if (GetAttr(node, "FixedRowHeight") is not null) row.FixedRowHeight = GetAttrLength(node, "FixedRowHeight");
        if (ParseVerticalAlignment(GetAttr(node, "VerticalAlignment")) is { } va) row.VerticalAlignment = va;
        if (string.Equals(GetAttr(node, "IsInNewPage"), "true", StringComparison.OrdinalIgnoreCase))
            row.IsInNewPage = true;
        if (string.Equals(GetAttr(node, "IsRowBroken"), "true", StringComparison.OrdinalIgnoreCase))
            row.IsRowBroken = true;

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Border": row.Border = ParseBorder(child); break;
                case "DefaultCellBorder": row.DefaultCellBorder = ParseBorder(child); break;
                case "DefaultCellPadding": row.DefaultCellPadding = ParseMargin(child); break;
                case "DefaultCellTextState":
                    ApplyRoundTripTextState(child, row.DefaultCellTextState ??= new TextState());
                    break;
                case "Cell": BuildRoundTripCell(row, child, images); break;
            }
        }
    }

    private static void BuildRoundTripCell(Row row, XmlNode node, List<Image> images)
    {
        var cell = row.Cells.Add();
        if (GetAttr(node, "ColSpan") is { } cs && int.TryParse(cs, out var span) && span > 0)
            cell.ColSpan = span;
        if (GetAttr(node, "RowSpan") is { } rs && int.TryParse(rs, out var rowSpan) && rowSpan > 0)
            cell.RowSpan = rowSpan;
        if (string.Equals(GetAttr(node, "IsNoBorder"), "true", StringComparison.OrdinalIgnoreCase))
            cell.IsNoBorder = true;
        if (GetAttr(node, "IsWordWrapped") is { } wordWrapped)
            cell.IsWordWrapped = string.Equals(wordWrapped, "true", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(GetAttr(node, "IsOverrideByFragment"), "true", StringComparison.OrdinalIgnoreCase))
            cell.IsOverrideByFragment = true;
        if (ParseColorValue(GetAttr(node, "BackgroundColor")) is { } bg) cell.BackgroundColor = bg;
        if (ParseHAlign(GetAttr(node, "Alignment")) is { } ha) cell.Alignment = ha;
        if (ParseVerticalAlignment(GetAttr(node, "VerticalAlignment")) is { } va) cell.VerticalAlignment = va;

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Border": cell.Border = ParseBorder(child); continue;
                case "Margin": cell.Margin = ParseMargin(child); continue;
                case "BackgroundImage":
                    if (FirstElementChild(child, "Image") is { } cellBg)
                        cell.BackgroundImage = BuildRoundTripImage(cellBg, images);
                    continue;
                case "DefaultCellTextState":
                    ApplyRoundTripTextState(child, cell.DefaultCellTextState ??= new TextState());
                    continue;
            }
            if (BuildRoundTripParagraph(child, images) is { } paragraph)
                cell.Paragraphs.Add(paragraph);
        }
    }

    // ---- boxes and images ------------------------------------------------------

    private static FloatingBox BuildRoundTripFloatingBox(XmlNode node, List<Image> images)
    {
        var box = new FloatingBox();
        ApplyRoundTripParagraph(box, node);
        // The shadowing set (see ApplyRoundTripParagraph).
        if (ParseHAlign(GetAttr(node, "HorizontalAlignment")) is { } boxHa) box.HorizontalAlignment = boxHa;
        if (ParseVerticalAlignment(GetAttr(node, "VerticalAlignment")) is { } boxVa) box.VerticalAlignment = boxVa;
        if (GetAttr(node, "ZIndex") is { } boxZ && int.TryParse(boxZ, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var boxZIndex))
            box.ZIndex = boxZIndex;
        if (GetAttr(node, "Width") is not null) box.Width = GetAttrLength(node, "Width");
        if (GetAttr(node, "Height") is not null) box.Height = GetAttrLength(node, "Height");
        // Written only for an absolute box, and written in page coordinates — the
        // content-area offset the authored-template reader adds is a reading of hand
        // markup, not something the DOM ever stored.
        if (GetAttr(node, "Left") is not null || GetAttr(node, "Top") is not null)
        {
            box.Left = GetAttrLength(node, "Left");
            box.Top = GetAttrLength(node, "Top");
            box.PositioningMode = ParagraphPositioningMode.Absolute;
        }
        if (ParseColorValue(GetAttr(node, "BackgroundColor")) is { } bg) box.BackgroundColor = bg;
        if (string.Equals(GetAttr(node, "IsNeedRepeating"), "false", StringComparison.OrdinalIgnoreCase))
            box.IsNeedRepeating = false;
        if (GetAttr(node, "ColumnCount") is { } cc && int.TryParse(cc, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var columnCount) && columnCount > 1)
        {
            box.ColumnInfo.ColumnCount = columnCount;
            if (GetAttr(node, "ColumnWidths") is { } cw) box.ColumnInfo.ColumnWidths = cw;
            if (GetAttr(node, "ColumnSpacing") is { } cspace) box.ColumnInfo.ColumnSpacing = cspace;
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Margin": box.Margin = ParseMargin(child); continue;
                case "Padding": box.Padding = ParseMargin(child); continue;
                case "Border": box.Border = ParseBorder(child); continue;
            }
            if (BuildRoundTripParagraph(child, images) is { } paragraph)
                box.Paragraphs.Add(paragraph);
        }
        return box;
    }

    private static Image BuildRoundTripImage(XmlNode node, List<Image> images)
    {
        var image = new Image();
        ApplyRoundTripParagraph(image, node);
        ApplyRoundTripMargin(node, m => image.Margin = m);
        if (GetAttr(node, "File") is { } file) image.File = file;
        if (GetAttr(node, "FixWidth") is not null) image.FixWidth = GetAttrLength(node, "FixWidth");
        if (GetAttr(node, "FixHeight") is not null) image.FixHeight = GetAttrLength(node, "FixHeight");
        if (GetAttr(node, "ImageScale") is not null) image.ImageScale = GetAttrDouble(node, "ImageScale");
        if (string.Equals(GetAttr(node, "IsBlackWhite"), "true", StringComparison.OrdinalIgnoreCase))
            image.IsBlackWhite = true;
        if (string.Equals(GetAttr(node, "IsApplyResolution"), "true", StringComparison.OrdinalIgnoreCase))
            image.IsApplyResolution = true;
        if (FirstElementChild(node, "Data") is { } data && data.InnerText is { Length: > 0 } base64)
            image.ImageStream = new MemoryStream(Convert.FromBase64String(base64));
        // Only a FILE image joins the pending list — that list exists so Save can check
        // the paths are still resolvable, and an inlined one has no path to check.
        if (!string.IsNullOrEmpty(image.File)) images.Add(image);
        return image;
    }
}
