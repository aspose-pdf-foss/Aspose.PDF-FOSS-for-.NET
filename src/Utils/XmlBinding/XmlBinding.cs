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

internal static partial class XmlBinding
{
    /// <summary>Per-bind state threaded through the element processors: the target
    /// document (for <c>id</c> registration), the template's base directory, and
    /// the <see cref="Image"/> objects built so far (their <c>File</c> paths are
    /// validated at Save time — validated LIVE, because a caller may resolve an
    /// id-registered image to a real path between BindXml and Save).</summary>
    private sealed class BindContext
    {
        public required Document Document { get; init; }
        public string? BaseDir { get; init; }
        public List<Image> Images { get; } = new();
        public XmlDefaults Defaults { get; } = new();
        public XmlNode? DocPageInfo { get; set; }
    }

    /// <summary>Document-level <c>&lt;PageInfo&gt;&lt;DefaultTextState&gt;</c>:
    /// the fallback font, size, colour and leading for every fragment.</summary>
    private sealed class XmlDefaults
    {
        public double FontSize;
        public string? FontName;
        public Color? Foreground;
        public double LineSpacing;
    }

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

        // A template this library WROTE (root RoundTrip="true") describes a DOM that
        // already exists, so it is read back literally. The rest of this binder
        // reconstructs intent from hand-authored markup — the XML-generator table
        // dialect, a box's Left/Top measured from the content area, the stylesheet
        // whitespace collapse in cells — and those calibrations would deform a DOM
        // that was never authored that way.
        if (string.Equals(GetAttr(root, "RoundTrip"), "true", StringComparison.OrdinalIgnoreCase))
        {
            // Re-read PRESERVING whitespace. A DOM segment may be nothing but a line
            // break (a blank spacer paragraph is written as Environment.NewLine), and
            // the default load drops a whitespace-only text node — the segment would
            // come back empty and the spacing with it.
            var exact = new XmlDocument { PreserveWhitespace = true };
            try { exact.LoadXml(xmlContent); }
            catch (System.Xml.XmlException) { /* keep the already-parsed tree */ }
            BindRoundTrip(document, exact.DocumentElement ?? root);
            return;
        }

        var ctx = new BindContext { Document = document, BaseDir = baseDir };

        // The document-level <PageInfo> supplies page defaults (margins, size)
        // and its <DefaultTextState …> the fallback font, size, colour and
        // inter-line leading for every fragment.
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Element && node.LocalName == "PageInfo")
            {
                ctx.DocPageInfo = node;
                foreach (XmlNode ic in node.ChildNodes)
                    if (ic.NodeType == XmlNodeType.Element && ic.LocalName == "DefaultTextState")
                    {
                        ctx.Defaults.LineSpacing = GetAttrLength(ic, "LineSpacing", 0);
                        ctx.Defaults.FontSize = GetAttrLength(ic, "FontSize", 0);
                        ctx.Defaults.FontName = GetAttr(ic, "Font");
                        ctx.Defaults.Foreground = ParseColorValue(GetAttr(ic, "ForegroundColor"));
                    }
            }
        }

        // Process <Page> elements. Auto-sequenced <Heading> numbering runs
        // document-wide, so the counters live for the whole bind.
        var headingCounters = new Dictionary<int, int>();
        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            if (node.LocalName == "Page")
                ProcessPage(document, node, ctx, headingCounters);
            // Skip PageInfo at document level (used for defaults)
        }

        // If no pages were added, add one
        if (document.PageCount == 0)
            document.Pages.Add();

        // Store deferred images for File validation during Save
        if (ctx.Images.Count > 0)
            document.PendingXmlImages = ctx.Images;
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
                    var resolved = FontRepository.TryFindFont(fontAttr);
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

    private static void ProcessPage(Document document, XmlNode pageNode, BindContext ctx, Dictionary<int, int>? headingCounters = null)
    {
        var page = document.Pages.Add();

        // A Page's id resolves to the Page object through Document.GetObjectById.
        if (GetId(pageNode) is { } pageId)
            document.RegisterXmlObject(pageId, page);

        // Document-level <PageInfo> defaults apply first; the page's own
        // <PageInfo> child (processed below) overrides them.
        if (ctx.DocPageInfo is { } docPi)
            ApplyPageInfo(page, docPi);

        // A page authored with inline-joined paragraphs or footnotes routes its
        // body through the styled-flow engine: consecutive body children collect
        // into dissolved FloatingBoxes whose layout performs the inline joins,
        // superscript footnote markers and the page-bottom footnote bands.
        var styledFlow = PageUsesStyledFlow(pageNode);
        FloatingBox? runBox = null;
        void FlushRun()
        {
            if (runBox is not null && runBox.Paragraphs.Count > 0)
                page.Paragraphs.Add(runBox);
            runBox = null;
        }

        // Process child elements (including Header/Footer which may contain tables)
        foreach (XmlNode child in pageNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (!styledFlow)
            {
                ProcessPageChild(document, page, child, ctx, headingCounters);
                continue;
            }
            switch (child.LocalName)
            {
                case "TextFragment":
                case "FootNote":
                    if (BuildPageFragment(document, child, ctx.Defaults, includeEmpty: true) is { } bodyFrag)
                        (runBox ??= new FloatingBox()).Paragraphs.Add(bodyFrag);
                    break;
                case "Heading":
                    if (BuildPageFragment(document, child, ctx.Defaults,
                            HeadingPrefix(child, headingCounters), includeEmpty: true) is { } headFrag)
                        (runBox ??= new FloatingBox()).Paragraphs.Add(headFrag);
                    break;
                case "Table":
                    (runBox ??= new FloatingBox()).Paragraphs.Add(BuildTable(child, ctx));
                    break;
                case "Graph":
                    FlushRun();
                    page.Paragraphs.Add(BuildGraph(child));
                    break;
                case "FloatingBox":
                    FlushRun();
                    ProcessFloatingBox(page, child, ctx);
                    break;
                case "Image":
                    FlushRun();
                    page.Paragraphs.Add(BuildImage(child, ctx));
                    break;
                default:
                    FlushRun();
                    ProcessPageChild(document, page, child, ctx, headingCounters);
                    break;
            }
        }
        FlushRun();
    }

    /// <summary>An auto-sequenced heading prints its hierarchical section number
    /// before the text ("1   Table of content"), counting per level across the
    /// whole document; a level-N heading restarts the deeper counters.</summary>
    private static string HeadingPrefix(XmlNode child, Dictionary<int, int>? headingCounters)
    {
        if (headingCounters is null
            || !string.Equals(GetAttr(child, "IsAutoSequence"), "true", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
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
        return parts.Count > 0 ? string.Join(".", parts) + "   " : string.Empty;
    }

    /// <summary>True when the page's body is authored in the styled-flow
    /// dialect: any inline-joined paragraph or footnote anywhere below it.</summary>
    private static bool PageUsesStyledFlow(XmlNode pageNode)
    {
        if (pageNode.NodeType == XmlNodeType.Element)
        {
            if (pageNode.LocalName == "FootNote") return true;
            if (string.Equals(GetAttr(pageNode, "IsInLineParagraph"), "true",
                    StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (XmlNode child in pageNode.ChildNodes)
            if (child.NodeType == XmlNodeType.Element && PageUsesStyledFlow(child))
                return true;
        return false;
    }

    // Dispatch one child of <Page> (or equivalently one child of a container such
    // as <FloatingBox> that participates in the page's flow). Kept as a separate
    // method so FloatingBox/FootNote flattening can reuse it.
    private static void ProcessPageChild(Document document, Page page, XmlNode child, BindContext ctx, Dictionary<int, int>? headingCounters = null)
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
                            {
                                // The band fragment keeps its alignment attribute, its
                                // inline flag and its font size (a right-aligned
                                // 10 pt print date; a band sizes from a
                                // TextState nested in the segment). An empty fragment
                                // is a line of the band too (a page number sitting
                                // under an empty centred paragraph).
                                var bandFrag = new TextFragment(bandText ?? string.Empty);
                                if (ParseHAlign(GetAttr(hf, "HorizontalAlignment")) is { } bandAlign)
                                    bandFrag.HorizontalAlignment = bandAlign;
                                if (string.Equals(GetAttr(hf, "IsInLineParagraph"), "true", StringComparison.OrdinalIgnoreCase))
                                    bandFrag.IsInLineParagraph = true;
                                foreach (XmlNode bandChild in hf.ChildNodes)
                                {
                                    if (bandChild.NodeType != XmlNodeType.Element) continue;
                                    var bandState = bandChild.LocalName == "TextState" ? bandChild
                                        : bandChild.LocalName == "TextSegment"
                                            ? FirstElementChild(bandChild, "TextState")
                                            : null;
                                    if (bandState is not null && GetAttr(bandState, "FontSize") is not null
                                        && bandFrag.TextState.FontSize == 10)
                                        bandFrag.TextState.FontSize =
                                            (float)GetAttrLength(bandState, "FontSize", 10);
                                }
                                band.Paragraphs.Add(bandFrag);
                            }
                            break;
                        }
                        case "Table":
                            band.Paragraphs.Add(BuildTable(hf, ctx));
                            break;
                        case "Image":
                            band.Paragraphs.Add(BuildImage(hf, ctx));
                            break;
                    }
                }
                if (child.LocalName == "Header") page.Header = band;
                else page.Footer = band;
                break;
            }
            case "Table":
                ProcessTable(page, child, ctx);
                break;
            case "TextFragment":
                ProcessTextFragment(document, page, child, ctx.Defaults);
                break;
            case "Image":
                page.Paragraphs.Add(BuildImage(child, ctx));
                break;
            case "FloatingBox":
                ProcessFloatingBox(page, child, ctx);
                break;
            case "Heading":
                // Headings here are styled paragraph text, not TOC entries.
                ProcessTextFragment(document, page, child, ctx.Defaults,
                    HeadingPrefix(child, headingCounters));
                break;
            case "Graph":
                page.Paragraphs.Add(BuildGraph(child));
                break;
            case "FootNote":
                // Inline the footnote body as flow text; a true footnote pass
                // would anchor it at the page foot, but getting the text onto
                // *some* page is the correct baseline for pagination.
                ProcessTextFragment(document, page, child, ctx.Defaults);
                break;
        }
    }

    // FloatingBox without explicit Left/Top gets inlined by the page's FlowLayout,
    // so emitting one with child paragraphs is enough for paginated flow. Absolute
    // boxes (Left/Top set) could be added here later; none of the regression XMLs
    // currently exercise that path.
    private static void ProcessFloatingBox(Page page, XmlNode boxNode, BindContext ctx)
    {
        var fbox = new FloatingBox();

        // Box geometry and background from attributes. A box carrying Left/Top is
        // absolutely positioned relative to the page's CONTENT area (the era
        // template shape: Top=100/Left=500 lands at margin.Top+100 /
        // margin.Left+500); one without stays in the flow (inlined by FlowLayout).
        // The content-area translation is the LAYOUT's job now (the absolute
        // branch of LayoutFloatingBoxParagraph adds the page margins, probed on
        // the generator API too) — pre-adding them here doubled the shift.
        if (GetAttr(boxNode, "Left") is not null || GetAttr(boxNode, "Top") is not null)
        {
            fbox.Left = GetAttrLength(boxNode, "Left");
            fbox.Top = GetAttrLength(boxNode, "Top");
            fbox.PositioningMode = ParagraphPositioningMode.Absolute;
        }
        var boxW = GetAttrLength(boxNode, "Width");
        if (boxW > 0) fbox.Width = boxW;
        var boxH = GetAttrLength(boxNode, "Height");
        if (boxH > 0) fbox.Height = boxH;
        if (ParseColorValue(GetAttr(boxNode, "BackgroundColor")) is { } boxBg)
            fbox.BackgroundColor = boxBg;

        foreach (XmlNode child in boxNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Margin":
                    fbox.Margin = ParseMargin(child);
                    break;
                case "TextFragment":
                case "FootNote":
                    if (BuildPageFragment(ctx.Document, child, ctx.Defaults, includeEmpty: true) is { } boxFrag)
                        fbox.Paragraphs.Add(boxFrag);
                    break;
                case "Heading":
                    if (BuildPageFragment(ctx.Document, child, ctx.Defaults, includeEmpty: true) is { } boxHead)
                        fbox.Paragraphs.Add(boxHead);
                    break;
                case "Table":
                {
                    var table = BuildTable(child, ctx);
                    fbox.Paragraphs.Add(table);
                    break;
                }
                case "Graph":
                    fbox.Paragraphs.Add(BuildGraph(child));
                    break;
                case "Image":
                    fbox.Paragraphs.Add(BuildImage(child, ctx));
                    break;
                case "FloatingBox":
                    // Nested box — flatten its paragraphs into the outer box so
                    // FlowLayout only ever inlines one level of nesting.
                    FlattenNestedFloatingBox(fbox, child, ctx);
                    break;
                // Line / All — visual primitives not yet renderable; ignore.
            }
        }
        page.Paragraphs.Add(fbox);
    }

    private static void FlattenNestedFloatingBox(FloatingBox outer, XmlNode innerNode,
        BindContext ctx)
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
                    outer.Paragraphs.Add(BuildTable(child, ctx));
                    break;
            }
        }
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

    /// <summary>Build the <see cref="Image"/> object for an <c>&lt;Image&gt;</c>
    /// element: File / FixWidth / FixHeight from attributes, id registration so a
    /// caller can resolve it through <see cref="Document.GetObjectById"/> and
    /// re-point <c>File</c> before saving (the intended flow). Every built image joins
    /// the pending list; Save validates the CURRENT File values.</summary>
    private static Image BuildImage(XmlNode imageNode, BindContext ctx)
    {
        var image = new Image();
        if (GetAttr(imageNode, "File") is { } file)
            image.File = file;
        var fw = GetAttrLength(imageNode, "FixWidth");
        if (fw > 0) image.FixWidth = fw;
        var fh = GetAttrLength(imageNode, "FixHeight");
        if (fh > 0) image.FixHeight = fh;
        if (GetId(imageNode) is { } id)
        {
            image.Id = id;
            ctx.Document.RegisterXmlObject(id, image);
        }
        ctx.Images.Add(image);
        return image;
    }

    /// <summary>An Aspose XML <c>&lt;Graph&gt;</c>: currently the line-rule shape
    /// (a blue separator under a chapter heading).</summary>
    private static Aspose.Pdf.Drawing.Graph BuildGraph(XmlNode node)
    {
        var graph = new Aspose.Pdf.Drawing.Graph(
            GetAttrLength(node, "Width", 100), GetAttrLength(node, "Height", 10));
        ApplyParagraphAttributes(graph, node);
        if (string.Equals(GetAttr(node, "IsChangePosition"), "false", StringComparison.OrdinalIgnoreCase))
            graph.IsChangePosition = false;
        if (GetAttr(node, "Left") is not null) graph.Left = GetAttrLength(node, "Left");
        if (GetAttr(node, "Top") is not null) graph.Top = GetAttrLength(node, "Top");
        if (GetAttr(node, "RotationAngle") is not null)
            graph.GraphInfo.RotationAngle = GetAttrDouble(node, "RotationAngle");
        if (GetAttr(node, "SkewAngleX") is not null)
            graph.GraphInfo.SkewAngleX = GetAttrDouble(node, "SkewAngleX");
        if (GetAttr(node, "SkewAngleY") is not null)
            graph.GraphInfo.SkewAngleY = GetAttrDouble(node, "SkewAngleY");
        if (GetAttr(node, "ScalingRateX") is not null)
            graph.GraphInfo.ScalingRateX = GetAttrDouble(node, "ScalingRateX");
        if (GetAttr(node, "ScalingRateY") is not null)
            graph.GraphInfo.ScalingRateY = GetAttrDouble(node, "ScalingRateY");
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "Margin": graph.Margin = ParseMargin(child); continue;
                case "Border": graph.Border = ParseBorder(child); continue;
            }
            if (BuildShape(child) is { } shape) graph.Shapes.Add(shape);
        }
        return graph;
    }

    /// <summary>The <see cref="BaseParagraph"/> attributes every paragraph element may
    /// carry — alignment, the flow flags and the z-order.</summary>
    private static void ApplyParagraphAttributes(BaseParagraph p, XmlNode node)
    {
        if (ParseHAlign(GetAttr(node, "HorizontalAlignment")) is { } ha) p.HorizontalAlignment = ha;
        if (ParseVerticalAlignment(GetAttr(node, "VerticalAlignment")) is { } va) p.VerticalAlignment = va;
        if (string.Equals(GetAttr(node, "IsInLineParagraph"), "true", StringComparison.OrdinalIgnoreCase))
            p.IsInLineParagraph = true;
        if (string.Equals(GetAttr(node, "IsInNewPage"), "true", StringComparison.OrdinalIgnoreCase))
            p.IsInNewPage = true;
        if (string.Equals(GetAttr(node, "IsKeptWithNext"), "true", StringComparison.OrdinalIgnoreCase))
            p.IsKeptWithNext = true;
        if (GetAttr(node, "ZIndex") is { } z && int.TryParse(z, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var zi))
            p.ZIndex = zi;
    }

    /// <summary>One shape from its element, or null when the element names no shape
    /// this binder builds. A <c>Path</c> recurses: it is a shape holding shapes, and
    /// its children are painted as one region under its own GraphInfo.</summary>
    private static Aspose.Pdf.Drawing.Shape? BuildShape(XmlNode child)
    {
        Aspose.Pdf.Drawing.Shape? shape = null;
        switch (child.LocalName)
        {
            case "Line":
            {
                var coords = ParseFloatList(GetAttr(child, "PositionArray"));
                if (coords.Count < 4) return null;
                shape = new Aspose.Pdf.Drawing.Line(coords.ToArray());
                break;
            }
            case "Arc":
            {
                var arc = new Aspose.Pdf.Drawing.Arc(
                    GetAttrLength(child, "PosX"), GetAttrLength(child, "PosY"),
                    GetAttrLength(child, "Radius"),
                    GetAttrLength(child, "Alpha"), GetAttrLength(child, "Beta"));
                // An elliptical arc names its second radius; a circular one does not,
                // and the ctor has already set both radii from Radius.
                if (GetAttr(child, "RadiusY") is not null)
                    arc.RadiusY = GetAttrLength(child, "RadiusY");
                shape = arc;
                break;
            }
            case "Circle":
                shape = new Aspose.Pdf.Drawing.Circle(
                    GetAttrLength(child, "PosX"), GetAttrLength(child, "PosY"),
                    GetAttrLength(child, "Radius"));
                break;
            case "Ellipse":
                shape = new Aspose.Pdf.Drawing.Ellipse(
                    GetAttrLength(child, "Left"), GetAttrLength(child, "Bottom"),
                    GetAttrLength(child, "Width"), GetAttrLength(child, "Height"));
                break;
            case "Rectangle":
                shape = new Aspose.Pdf.Drawing.Rectangle(
                    GetAttrLength(child, "Left"), GetAttrLength(child, "Bottom"),
                    GetAttrLength(child, "Width"), GetAttrLength(child, "Height"))
                {
                    RoundedCornerRadius = GetAttrLength(child, "RoundedCornerRadius"),
                };
                break;
            case "Curve":
            {
                var pts = ParseFloatList(GetAttr(child, "PositionArray"));
                if (pts.Count < 8) return null;
                shape = new Aspose.Pdf.Drawing.Curve(
                    pts[0], pts[1], pts[2], pts[3], pts[4], pts[5], pts[6], pts[7]);
                break;
            }
            case "Path":
            {
                var path = new Aspose.Pdf.Drawing.Path();
                foreach (XmlNode sub in child.ChildNodes)
                {
                    if (sub.NodeType != XmlNodeType.Element) continue;
                    if (BuildShape(sub) is { } inner) path.Shapes.Add(inner);
                }
                shape = path;
                break;
            }
            default:
                return null;
        }

        foreach (XmlNode gi in child.ChildNodes)
        {
            if (gi.NodeType != XmlNodeType.Element || gi.LocalName != "GraphInfo") continue;
            var lw = GetAttrLength(gi, "LineWidth");
            if (lw > 0) shape.GraphInfo.LineWidth = (float)lw;
            if (ParseColorValue(GetAttr(gi, "Color")) is { } gc)
                shape.GraphInfo.Color = gc;
            if (ParseColorValue(GetAttr(gi, "FillColor")) is { } fc)
                shape.GraphInfo.FillColor = fc;
            if (ParseFloatList(GetAttr(gi, "DashArray")) is { Count: > 0 } dashes)
            {
                shape.GraphInfo.DashArray = dashes.Select(d => (int)d).ToArray();
                shape.GraphInfo.DashPhase = (int)GetAttrDouble(gi, "DashPhase");
            }
        }
        return shape;
    }

}
