using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static partial class XmlBinding
{
    private static void ProcessTextFragment(Document document, Page page, XmlNode fragNode, XmlDefaults defaults, string textPrefix = "")
    {
        var tf = BuildPageFragment(document, fragNode, defaults, textPrefix, includeEmpty: false);
        if (tf is not null)
            page.Paragraphs.Add(tf);
    }

    /// <summary>Build one page-level (or FloatingBox-level) TextFragment from
    /// its node: structured segments, TabStops, margins, alignment, the
    /// IsInLineParagraph flag and an attached FootNote. Returns null for a
    /// text-less fragment unless <paramref name="includeEmpty"/> — inside the
    /// styled flow an empty fragment is a deliberate blank line.</summary>
    private static TextFragment? BuildPageFragment(Document document, XmlNode fragNode, XmlDefaults defaults,
        string textPrefix = "", bool includeEmpty = false)
    {
        RegisterSegmentIds(document, fragNode);
        var id = GetId(fragNode);

        var tf = new TextFragment { XmlGeneratorModel = true };

        // Fragment-level styling: the <TextState> child of the fragment (also the
        // shape wrapping the segments — a template may nest <TextSegment> INSIDE the
        // state element), FontSize/HorizontalAlignment attributes on the
        // fragment itself, and the document DefaultTextState as the fallback.
        // A fragment-level <TextState> element REPLACES the document defaults
        // wholesale — unspecified properties fall back to the schema defaults
        // (10 pt, Helvetica, black, no leading), NOT to the DefaultTextState
        // (under a 9 pt / LineSpacing 4 document default,
        // colour-only fragment states render 10 pt bodies on a bare 10 pt pitch,
        // and the 20 pt title's leading blank line is 10 pt tall). A fragment
        // with NO TextState of its own takes the document defaults (e.g.
        // 12 pt + 4 leading bodies).
        var hasFragState = false;
        foreach (XmlNode child in fragNode.ChildNodes)
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "TextState"
                && !HasElementChild(child, "TextSegment"))
            { hasFragState = true; break; }

        var fragState = hasFragState
            ? new XmlTextStyle { FontSize = 10, FontSizeSet = true }
            : new XmlTextStyle
            {
                FontSize = defaults.FontSize > 0 ? defaults.FontSize : 10,
                FontSizeSet = true, // a page fragment always resolves a concrete size
                FontName = defaults.FontName,
                Foreground = defaults.Foreground,
            };
        TabStops? tabStops = null;
        MarginInfo? margin = null;
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "TextState":
                    // A TextState WRAPPING segments styles only
                    // those segments — it must not bleed into the fragment level.
                    if (!HasElementChild(child, "TextSegment"))
                        ReadXmlTextStyle(child, fragState, segmentNested: false);
                    tabStops ??= ParseTabStops(child);
                    break;
                case "Margin":
                    margin = ParseMargin(child);
                    break;
            }
        }
        // NOTE: a FontSize ATTRIBUTE on <TextFragment> is not schema — the
        // binder ignores it (FontSize="8" cells
        // render at the 10 pt default); only a nested <TextState> sizes text.
        if (ParseHAlign(GetAttr(fragNode, "HorizontalAlignment")) is { } ha)
            tf.HorizontalAlignment = ha;
        if (string.Equals(GetAttr(fragNode, "IsInLineParagraph"), "true", StringComparison.OrdinalIgnoreCase))
            tf.IsInLineParagraph = true;

        tf.TabStops = tabStops;
        tf.Margin = margin ?? new MarginInfo();
        ApplyXmlStyle(tf.TextState, fragState);
        // The document leading only reaches fragments WITHOUT their own
        // TextState (see the replacement rule above); a fragment state may still
        // declare its own LineSpacing, applied by ApplyXmlStyle.
        if (!hasFragState && defaults.LineSpacing > 0)
            tf.TextState.LineSpacing = (float)defaults.LineSpacing;

        // Segments, in document order. Two authored shapes:
        //   <TextSegment>…(<TextState/>)…</TextSegment>       — state nested in segment
        //   <TextState …><TextSegment>…</TextSegment></TextState> — state wraps segments
        if (!string.IsNullOrEmpty(textPrefix))
            AddXmlSegment(tf, textPrefix, fragState);
        var any = false;
        var authoredSegments = 0;
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextSegment")
            {
                authoredSegments++;
                any |= AddXmlSegmentFromNode(tf, child, fragState);
            }
            else if (child.LocalName == "TextState")
            {
                foreach (XmlNode wrapped in child.ChildNodes)
                {
                    if (wrapped.NodeType != XmlNodeType.Element || wrapped.LocalName != "TextSegment") continue;
                    authoredSegments++;
                    var wrapStyle = fragState.Clone();
                    ReadXmlTextStyle(child, wrapStyle, segmentNested: false);
                    any |= AddXmlSegmentFromNode(tf, wrapped, wrapStyle);
                }
            }
        }
        // A fragment authored without any segment is a shell that takes no room;
        // one whose only segment is empty still stands one default line tall.
        tf.XmlEmptyShell = authoredSegments == 0 && string.IsNullOrEmpty(textPrefix);

        // A nested <FootNote>: custom marker label from <Text Text="…"/> plus the
        // note body's own styled fragments (inline joins included) — rendered as
        // a superscript reference at the anchor and a page-bottom band.
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "FootNote") continue;
            var note = new Note();
            foreach (XmlNode fnChild in child.ChildNodes)
            {
                if (fnChild.NodeType != XmlNodeType.Element) continue;
                switch (fnChild.LocalName)
                {
                    case "Text":
                        note.Text = GetAttr(fnChild, "Text");
                        break;
                    case "TextFragment":
                        // Empty fragments stay (as XmlEmptyShell): the note's last
                        // one closes the note with an empty line in the band.
                        if (BuildPageFragment(document, fnChild, defaults, includeEmpty: true) is { } noteFrag)
                            note.Paragraphs.Add(noteFrag);
                        break;
                }
            }
            if (note.Text is not null || note.Paragraphs.Count > 0)
                tf.FootNote = note;
            break;
        }

        if (id is not null)
        {
            tf.Id = id;
            document.RegisterXmlObject(id, tf);
        }
        if (!any && string.IsNullOrEmpty(textPrefix) && !includeEmpty && tf.FootNote is null)
            return null; // a text-less fragment produces no layout
        return tf;
    }

    /// <summary>One segment from its <c>&lt;TextSegment&gt;</c> node: verbatim text
    /// (entities decoded, whitespace preserved — the XML-generator line model
    /// renders newlines and spaces as authored), styled by the segment's own
    /// nested <c>&lt;TextState&gt;</c> over the inherited style. Cell segments
    /// (<paramref name="normalizeCellWhitespace"/>) instead collapse the XSLT
    /// stylesheet's indentation to single spaces — the table engine keeps its
    /// own calibrated whitespace model (see MaxCellBlankLines). Returns whether
    /// the segment carries any text.</summary>
    private static bool AddXmlSegmentFromNode(TextFragment tf, XmlNode segNode, XmlTextStyle inherited,
        bool normalizeCellWhitespace = false)
    {
        var style = inherited.Clone();
        foreach (XmlNode sc in segNode.ChildNodes)
        {
            if (sc.NodeType == XmlNodeType.Element && sc.LocalName == "TextState")
                ReadXmlTextStyle(sc, style, segmentNested: true);
        }
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode textChild in segNode.ChildNodes)
        {
            if (textChild.NodeType is XmlNodeType.Text or XmlNodeType.CDATA
                or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                sb.Append(DecodeXmlEntities(textChild.Value ?? ""));
        }
        var text = sb.ToString();
        if (normalizeCellWhitespace)
        {
            // In-cell #$TAB tokens render no glyphs but keep their line (a
            // tab-only fragment reserves a default-size line between the two
            // styled ones); the table engine has no pen to advance.
            text = text.Replace("#$TAB", " ");
            if (text.IndexOfAny(new[] { '\n', '\r', '\t' }) >= 0)
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length == 0 && sb.Length > 0 && sb.ToString().Contains("#$TAB"))
                text = " ";
        }
        if (text.Length == 0) return false;
        AddXmlSegment(tf, text, style);
        return true;
    }

    private static void AddXmlSegment(TextFragment tf, string text, XmlTextStyle style)
    {
        var seg = new TextSegment(text);
        ApplyXmlStyle(seg.TextState, style);
        tf.Segments.Add(seg);
    }

    /// <summary>Resolved XML text styling, inherited document → fragment → segment.
    /// <see cref="FontSizeSet"/> tracks whether any level actually declared a
    /// size — an undeclared cell fragment must stay open to the table's
    /// DefaultCellTextState rather than pinning the 10 pt fallback.</summary>
    private sealed class XmlTextStyle
    {
        public double FontSize = 10;
        public bool FontSizeSet;
        public string? FontName;
        public Color? Foreground;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public double LineSpacing = -1; // −1 = not declared at this level
        public XmlTextStyle Clone() => (XmlTextStyle)MemberwiseClone();
    }

    private static void ApplyXmlStyle(TextState ts, XmlTextStyle style)
    {
        if (style.FontSizeSet) ts.FontSize = (float)style.FontSize;
        if (style.FontName is not null) ts.FontName = style.FontName;
        if (style.Foreground is not null) ts.ForegroundColor = style.Foreground;
        if (style.Bold) ts.IsBold = true;
        if (style.Italic) ts.IsItalic = true;
        if (style.Underline) ts.Underline = true;
        if (style.LineSpacing >= 0) ts.LineSpacing = (float)style.LineSpacing;
    }

    /// <summary>Read one <c>&lt;TextState&gt;</c> element's attributes into
    /// <paramref name="style"/>. <paramref name="segmentNested"/> marks the shape
    /// where the state element sits INSIDE a <c>&lt;TextSegment&gt;</c>: that
    /// deserialization path resolves an explicit Font name through the system
    /// repository, where the Helvetica names land on the Times faces
    /// (Font="Helvetica-Bold" measures and renders as Times-Bold) - the
    /// fragment-level shape keeps the literal base-font mapping (a wrapping
    /// shape stays Helvetica-Bold).</summary>
    private static void ReadXmlTextStyle(XmlNode node, XmlTextStyle style, bool segmentNested)
    {
        if (GetAttr(node, "FontSize") is not null)
        {
            style.FontSize = GetAttrLength(node, "FontSize", style.FontSize);
            style.FontSizeSet = true;
        }
        if (GetAttr(node, "Font") is { } font && !string.IsNullOrEmpty(font))
        {
            style.FontName = segmentNested ? MapXmlSegmentFont(font) : font;
            if (font.Contains("Bold", StringComparison.OrdinalIgnoreCase)) style.Bold = true;
            if (font.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                || font.Contains("Oblique", StringComparison.OrdinalIgnoreCase)) style.Italic = true;
        }
        if (ParseColorValue(GetAttr(node, "ForegroundColor")) is { } fg)
            style.Foreground = fg;
        // FontStyle: numeric bold/italic flags (1 = bold, 2 = italic, 3 = both).
        if (GetAttr(node, "FontStyle") is { } fsAttr && int.TryParse(fsAttr, out var fsv))
        {
            if ((fsv & 1) != 0) style.Bold = true;
            if ((fsv & 2) != 0) style.Italic = true;
        }
        if (string.Equals(GetAttr(node, "Underline"), "true", StringComparison.OrdinalIgnoreCase))
            style.Underline = true;
        if (GetAttr(node, "LineSpacing") is not null)
            style.LineSpacing = GetAttrLength(node, "LineSpacing", style.LineSpacing);
    }

    private static string MapXmlSegmentFont(string font) => font switch
    {
        "Helvetica" => "Times-Roman",
        "Helvetica-Bold" => "Times-Bold",
        "Helvetica-Oblique" => "Times-Italic",
        "Helvetica-BoldOblique" => "Times-BoldItalic",
        _ => font,
    };

    /// <summary>Parse a <c>&lt;TabStops&gt;</c> child of a TextState element.</summary>
    private static TabStops? ParseTabStops(XmlNode textStateNode)
    {
        foreach (XmlNode child in textStateNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "TabStops") continue;
            var stops = new TabStops();
            foreach (XmlNode stopNode in child.ChildNodes)
            {
                if (stopNode.NodeType != XmlNodeType.Element || stopNode.LocalName != "TabStop") continue;
                var stop = stops.Add((float)GetAttrLength(stopNode, "Position"));
                switch (GetAttr(stopNode, "AlignmentType"))
                {
                    case "Center": stop.AlignmentType = TabAlignmentType.Center; break;
                    case "Right": stop.AlignmentType = TabAlignmentType.Right; break;
                }
                switch (GetAttr(stopNode, "LeaderType"))
                {
                    case "Solid": stop.LeaderType = TabLeaderType.Solid; break;
                    case "Dash": stop.LeaderType = TabLeaderType.Dash; break;
                    case "Dot": stop.LeaderType = TabLeaderType.Dot; break;
                }
            }
            return stops.Count > 0 ? stops : null;
        }
        return null;
    }

    /// <summary>Cell fragments, one per authored line: a single-styled fragment
    /// splits on its <c>#$NL</c> tokens into stacked one-line fragments — a
    /// token between texts breaks the line, a doubled or trailing token yields
    /// a blank spacer line (a month header with a doubled token shows the spacer).</summary>
    private static IEnumerable<TextFragment> BuildStyledTextFragments(XmlNode fragNode, BindContext ctx)
    {
        var frag = BuildStyledTextFragment(fragNode, ctx);
        if (frag is null) yield break;
        var text = frag.Text ?? string.Empty;
        if (frag.Segments.Count != 1 || !text.Contains("#$NL", StringComparison.Ordinal))
        {
            yield return frag;
            yield break;
        }
        foreach (var lineText in text.Split(new[] { "#$NL" }, StringSplitOptions.None))
        {
            var lineFrag = new TextFragment(lineText) { XmlGeneratorModel = true };
            var st = frag.TextState;
            if (st.FontSizeTouched) lineFrag.TextState.FontSize = st.FontSize;
            if (st.FontName is not null) lineFrag.TextState.FontName = st.FontName;
            if (st.ForegroundColor is not null) lineFrag.TextState.ForegroundColor = st.ForegroundColor;
            if (st.IsBold) lineFrag.TextState.IsBold = true;
            if (st.IsItalic) lineFrag.TextState.IsItalic = true;
            yield return lineFrag;
        }
    }

    // Build a TextFragment for a cell from its <TextFragment> node. Both authored
    // shapes contribute segments — a nested <TextState> styles its own wrapped
    // segments (the idiom that puts every bold cell text INSIDE the state
    // element), a fragment-level one styles the rest. Returns null when the
    // fragment carries no text (an empty header cell).
    private static TextFragment? BuildStyledTextFragment(XmlNode fragNode, BindContext ctx)
    {
        RegisterSegmentIds(ctx.Document, fragNode);

        // The document default size seeds the table's DefaultCellTextState (see
        // ConfigureTable), NOT the fragment: an explicit table
        // <DefaultCellTextState FontSize=…> must beat the document default
        // (cells sized 8 beat a 9 pt document default).
        var fragState = new XmlTextStyle
        {
            FontSize = ctx.Defaults.FontSize > 0 ? ctx.Defaults.FontSize : 10,
            FontSizeSet = false,
            FontName = ctx.Defaults.FontName,
            Foreground = ctx.Defaults.Foreground,
        };
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "TextState"
                && !HasElementChild(child, "TextSegment"))
                ReadXmlTextStyle(child, fragState, segmentNested: false);
        }
        // (A FontSize ATTRIBUTE on the fragment is ignored — see ProcessTextFragment.)

        var staged = new TextFragment();
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextSegment")
            {
                AddXmlSegmentFromNode(staged, child, fragState, normalizeCellWhitespace: true);
            }
            else if (child.LocalName == "TextState")
            {
                foreach (XmlNode wrapped in child.ChildNodes)
                {
                    if (wrapped.NodeType != XmlNodeType.Element || wrapped.LocalName != "TextSegment") continue;
                    var wrapStyle = fragState.Clone();
                    ReadXmlTextStyle(child, wrapStyle, segmentNested: false);
                    AddXmlSegmentFromNode(staged, wrapped, wrapStyle, normalizeCellWhitespace: true);
                }
            }
        }
        var textSegs = new List<TextSegment>();
        foreach (var s in staged.Segments)
            if (!string.IsNullOrEmpty(s.Text)) textSegs.Add(s);
        if (textSegs.Count == 0) return null;

        // The common single-styled cell collapses to a PLAIN fragment carrying
        // the style at fragment level — a multi-segment fragment would route the
        // cell into the inline-layout path, whose packing model is not the XML
        // generator's (its right-aligned runs anchor differently).
        TextFragment tf;
        if (textSegs.Count == 1)
        {
            tf = new TextFragment(textSegs[0].Text) { XmlGeneratorModel = true };
            var st = textSegs[0].TextState;
            if (st.FontSizeTouched) tf.TextState.FontSize = st.FontSize;
            if (st.FontName is not null) tf.TextState.FontName = st.FontName;
            if (st.ForegroundColor is not null) tf.TextState.ForegroundColor = st.ForegroundColor;
            if (st.IsBold) tf.TextState.IsBold = true;
            if (st.IsItalic) tf.TextState.IsItalic = true;
            if (st.Underline)
            {
                tf.TextState.Underline = true;
                tf.HtmlUnderline = true; // the cell line renderer reads this flag
            }
        }
        else
        {
            tf = new TextFragment { XmlGeneratorModel = true };
            foreach (var s in textSegs) tf.Segments.Add(s);
            ApplyXmlStyle(tf.TextState, fragState);
        }
        if (GetId(fragNode) is { } id)
        {
            tf.Id = id;
            ctx.Document.RegisterXmlObject(id, tf);
        }
        return tf;
    }

    /// <summary>Register every id-carrying <c>&lt;TextSegment&gt;</c> under the
    /// fragment so <see cref="Document.GetObjectById"/> resolves it (callers cast
    /// the result to <see cref="TextSegment"/> and expect it non-null).</summary>
    private static void RegisterSegmentIds(Document document, XmlNode fragNode)
    {
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "TextSegment") continue;
            if (GetId(child) is { } segId)
                document.RegisterXmlObject(segId, new TextSegment(child.InnerText ?? string.Empty));
        }
    }
}
