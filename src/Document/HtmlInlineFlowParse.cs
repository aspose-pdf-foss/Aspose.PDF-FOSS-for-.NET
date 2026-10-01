using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The style an element imposes on the text inside it; every property
    /// inherits down the element tree.</summary>
    private sealed class HtmlInlineStyle
    {
        public string? Family;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public double SizePt;
        public Color? Fore;
        public Color? Back;
        public string? Href;
        public HtmlInlineStyle Clone() => (HtmlInlineStyle)MemberwiseClone();
    }

    /// <summary>What a flow block is: an anonymous run of inline content, a paragraph or
    /// division, a heading, or a list item.</summary>
    private enum HtmlFlowBlockKind { Anonymous, Paragraph, Div, Heading, ListItem }

    /// <summary>One block of the UA flow: its inline pieces and the box rules its tag set.</summary>
    private sealed class HtmlFlowBlock
    {
        public HtmlFlowBlockKind Kind;
        public List<HtmlInlineItem> Items = new();
        /// <summary>An anonymous piece that holds an element tag (even a closing one).</summary>
        public bool HasElement;
        public HorizontalAlignment? Align;
        public double HeightPt;
        public double IndentPt;
        public string? Marker;
        /// <summary>Margins in points, collapsed with the neighbours' by the larger.</summary>
        public double MarginTop;
        public double MarginBottom;
        public double FontPt;
        /// <summary>Style stack depth to restore when the block closes.</summary>
        public int StyleDepth;
    }

    /// <summary>Parser state for one fragment.</summary>
    private sealed class HtmlFlowParseState
    {
        public List<HtmlFlowBlock> blocks = new();
        public HtmlFlowBlock cur = new();
        public List<HtmlInlineStyle> stack = new() { new() };
        public List<(bool Ordered, int Counter, double Indent)> lists = new();
        public HtmlFragmentLayoutState hl = null!;
        public double basePt;
        public bool paragraphMargins;
        public bool firstElementSeen;
        public bool firstElementIsControl;
        public HtmlInlineStyle? firstElementStyle;
        /// <summary>The tag of a hidden (display: none) element being skipped, and how deep
        /// inside same-named elements the scan is; null when nothing is hidden.</summary>
        public string? hiddenTag;
        public int hiddenDepth;
    }

    private static readonly Regex HtmlInlineTagName = new(@"^</?\s*([A-Za-z][A-Za-z0-9]*)", RegexOptions.Compiled);
    private static readonly Regex HtmlInlineWhitespace = new(@"[ \t\r\n\f]+", RegexOptions.Compiled);
    private static readonly Regex HtmlInlineComment = new(@"<!--.*?-->|<!DOCTYPE[^>]*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlInlineHeadBlock = new(@"<(style|script|head|title)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    /// <summary>Tags the UA flow has no model for: their fragment keeps the calibrated engines.</summary>
    private static readonly Regex HtmlUnsupportedFlowTag = new(
        @"<\s*(table|tr|td|th|blockquote|hr|form|textarea|select|pre|center|dl|dt|dd|svg|iframe|object|section|article|header|footer|nav|aside|main)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>A style sheet the flow can honour: at most a <c>body</c> rule.</summary>
    private static readonly Regex HtmlBodyOnlyStyle = new(@"^<style\b[^>]*>\s*(?:body\s*\{[^}]*\}\s*)?</style\s*>$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>Whether the fragment holds only content the UA flow models: inline text,
    /// emphasis, spans, fonts, links, breaks, pictures, controls, paragraphs, divisions,
    /// headings and lists, under optional document wrappers and a body-only style sheet.</summary>
    private static bool IsUaFlowHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;
        var s = HtmlInlineComment.Replace(html, "");
        foreach (Match sm in Regex.Matches(s, @"<style\b.*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            if (!HtmlBodyOnlyStyle.IsMatch(sm.Value) && !IsTableOnlyStyleSheet(sm.Value)) return false;
        if (Regex.IsMatch(s, @"\bclass\s*=", RegexOptions.IgnoreCase)) return false;
        // A tag broken open inside another (</<strong>) keeps the calibrated engines.
        if (s.Contains("</<")) return false;
        if (HtmlUnsupportedFlowTag.IsMatch(s)) return false;
        foreach (Match tm in Regex.Matches(s, @"<([a-zA-Z][a-zA-Z0-9]*)\b[^>]*\bstyle\s*=\s*(['""])(.*?)\2",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var block = Regex.IsMatch(tm.Groups[1].Value, @"^(p|div|h[1-6]|li|ul|ol|body|html)$", RegexOptions.IgnoreCase);
            if (HasUnsupportedFlowCss(tm.Groups[3].Value, block)) return false;
        }
        return true;
    }

    /// <summary>CSS properties the flow has no model for on a block (box geometry, backgrounds,
    /// line-height) and on an inline element (box geometry, line-height). A block's top and
    /// bottom margins, a zero text-indent and a hidden element (display: none) are modelled.</summary>
    private const string HtmlUnsupportedBlockCss =
        @"(?<![-\w])(padding|background|border|width|float|line-height|position|vertical-align)\b";
    private const string HtmlUnsupportedInlineCss =
        @"(?<![-\w])(padding|margin|border|width|float|line-height|position)\b";

    private static bool HasUnsupportedFlowCss(string style, bool block)
    {
        if (Regex.IsMatch(style, block ? HtmlUnsupportedBlockCss : HtmlUnsupportedInlineCss, RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(style, @"(?<![-\w])display\s*:", RegexOptions.IgnoreCase) && !IsHiddenCss(style)) return true;
        if (!block) return false;
        var ti = Regex.Match(style, @"(?<![-\w])text-indent\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (ti.Success && CssMarginPt(ti.Groups[1].Value.Trim(), UaInlineBasePt) != 0) return true;
        var (_, _, left, right) = BlockCssMargins(style, UaInlineBasePt);
        return (!double.IsNaN(left) && left != 0) || (!double.IsNaN(right) && right != 0);
    }

    /// <summary>A piece of markup with nothing to lay out: only document wrappers, head
    /// blocks, comments and whitespace (a section's sheet and tags around its table).</summary>
    private static bool IsContentFreeHtml(string html)
    {
        var s = HtmlInlineHeadBlock.Replace(HtmlInlineComment.Replace(html, ""), "");
        s = Regex.Replace(s, @"</?\s*(html|body|head|meta|link)\b[^>]*>", "", RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(s);
    }

    /// <summary>display: none on an element hides it and everything inside it.</summary>
    private static bool IsHiddenCss(string style) =>
        Regex.IsMatch(style, @"(?<![-\w])display\s*:\s*none\b", RegexOptions.IgnoreCase);

    /// <summary>A style sheet whose rules select only table parts (which the flow never lays
    /// out: a piece holding a table keeps the calibrated engines) or the body binds nothing
    /// the flow sets, so a section report's table sheet does not disqualify its prose pieces.</summary>
    private static bool IsTableOnlyStyleSheet(string styleElement)
    {
        var css = Regex.Replace(Regex.Replace(styleElement, @"^<style\b[^>]*>|</style\s*>$", "", RegexOptions.IgnoreCase), @"/\*.*?\*/", "", RegexOptions.Singleline);
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{[^{}]*\}"))
            foreach (var sel in rule.Groups[1].Value.Split(','))
                if (!HtmlTableOnlySelector.IsMatch(sel.Trim()) && !sel.Trim().Equals("body", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static readonly Regex HtmlTableOnlySelector = new(
        @"^(?:(?:table|thead|tbody|tfoot|tr|td|th|caption)(?:[#.][\w-]+)*)(?:\s+(?:table|thead|tbody|tfoot|tr|td|th|caption)(?:[#.][\w-]+)*)*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A block's CSS margins in points (top, bottom, left, right): the shorthand's
    /// 1-4 values, then the longhands; em of the block's font size, a unitless value in points;
    /// NaN where the style sets no margin on that side.</summary>
    private static (double Top, double Bottom, double Left, double Right) BlockCssMargins(string style, double fontPt)
    {
        double top = double.NaN, bottom = double.NaN, left = double.NaN, right = double.NaN;
        var sh = Regex.Match(style, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (sh.Success)
        {
            var v = sh.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (v.Length >= 1)
            {
                top = CssMarginPt(v[0], fontPt);
                right = CssMarginPt(v[v.Length > 1 ? 1 : 0], fontPt);
                bottom = CssMarginPt(v[v.Length > 2 ? 2 : 0], fontPt);
                left = CssMarginPt(v[v.Length > 3 ? 3 : v.Length > 1 ? 1 : 0], fontPt);
            }
        }
        foreach (var (name, side) in new[] { ("top", 0), ("bottom", 1), ("left", 2), ("right", 3) })
        {
            var lm = Regex.Match(style, @"(?<![-\w])margin-" + name + @"\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (!lm.Success) continue;
            var pt = CssMarginPt(lm.Groups[1].Value.Trim(), fontPt);
            switch (side) { case 0: top = pt; break; case 1: bottom = pt; break; case 2: left = pt; break; default: right = pt; break; }
        }
        return (top, bottom, left, right);
    }

    /// <summary>One CSS margin value in points; "auto" and an unreadable value count as none (NaN).</summary>
    private static double CssMarginPt(string value, double fontPt)
    {
        var m = Regex.Match(value, @"^(-?[\d.]+)\s*(px|pt|em|%)?$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return double.NaN;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => v * CssPxToPt,
            "em" => v * fontPt,
            "%" => double.NaN,
            _ => v,
        };
    }

    /// <summary>The body rule of a body-only style sheet: its family and size.</summary>
    private static (string? Family, double SizePt) HtmlBodyStyleRule(string html)
    {
        var m = Regex.Match(html, @"<style\b[^>]*>\s*body\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return (null, 0);
        var st = new HtmlInlineStyle();
        ApplyInlineCssStyle(st, m.Groups[1].Value, '\0');
        return (st.Family, st.SizePt);
    }

    /// <summary>Split a fragment into flow blocks of styled pieces. Whitespace collapses to
    /// one space (a no-break space stays content), entities decode, a run takes the style of
    /// the elements enclosing it; a block tag closes the anonymous piece before it, a
    /// paragraph opening inside a paragraph closes that paragraph first.</summary>
    private static List<HtmlFlowBlock> ParseHtmlFlowBlocks(string html, HtmlFragmentLayoutState hl, double basePt,
        out bool firstElementIsControl, out HtmlInlineStyle? firstElementStyle)
    {
        var ps = new HtmlFlowParseState { hl = hl, basePt = basePt, paragraphMargins = hl.html.IsParagraphHasMargin };
        var s = HtmlInlineHeadBlock.Replace(HtmlInlineComment.Replace(html, ""), "");
        var i = 0;
        var n = s.Length;
        while (i < n)
        {
            if (s[i] != '<')
            {
                var j = s.IndexOf('<', i);
                if (j < 0) j = n;
                if (ps.hiddenTag is null) AddInlineText(ps.cur.Items, ps.stack[^1], s[i..j]);
                i = j;
                continue;
            }
            var end = s.IndexOf('>', i);
            if (end < 0) break;
            var tagStr = s[i..(end + 1)];
            i = end + 1;
            ParseFlowTag(ps, tagStr);
        }
        FlushFlowBlock(ps, trailing: true);
        firstElementIsControl = ps.firstElementIsControl;
        firstElementStyle = ps.firstElementStyle;
        return ps.blocks;
    }

    private static void ParseFlowTag(HtmlFlowParseState ps, string tagStr)
    {
        var nm = HtmlInlineTagName.Match(tagStr);
        if (!nm.Success) return;
        var tag = nm.Groups[1].Value.ToLowerInvariant();
        var isClose = tagStr[1] == '/';
        var selfClosed = tagStr.EndsWith("/>", StringComparison.Ordinal);
        if (tag is "html" or "body" or "head" or "meta" or "link" or "title") return;
        if (SkipHiddenElement(ps, tag, tagStr, isClose, selfClosed)) return;
        if (!isClose && !ps.firstElementSeen)
        {
            ps.firstElementSeen = true;
            ps.firstElementIsControl = tag == "input";
            ps.firstElementStyle = InlineElementStyle(ps.stack[^1], tagStr, tag);
        }
        ps.cur.HasElement = true;
        switch (tag)
        {
            case "br": ps.cur.Items.Add(new HtmlInlineItem { Kind = HtmlInlineKind.Break }); return;
            case "img": if (!isClose) AddInlineImage(ps.cur.Items, ps.stack[^1], tagStr, ps.hl); return;
            case "input": if (!isClose) AddInlineControl(ps.cur.Items, tagStr); return;
            case "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li":
                if (isClose) CloseFlowBlock(ps); else OpenFlowBlock(ps, tag, tagStr);
                return;
            case "ul" or "ol":
                if (isClose) CloseFlowList(ps); else OpenFlowList(ps, tag == "ol");
                return;
        }
        if (isClose) { if (ps.stack.Count > 1) ps.stack.RemoveAt(ps.stack.Count - 1); }
        else if (!selfClosed) ps.stack.Add(InlineElementStyle(ps.stack[^1], tagStr, tag));
    }

    /// <summary>A hidden element (display: none) and everything inside it lays out nothing:
    /// its tags and text are skipped up to the close matching its open.</summary>
    private static bool SkipHiddenElement(HtmlFlowParseState ps, string tag, string tagStr, bool isClose, bool selfClosed)
    {
        if (ps.hiddenTag is { } hidden)
        {
            if (tag == hidden && !selfClosed) ps.hiddenDepth += isClose ? -1 : 1;
            if (ps.hiddenDepth == 0) ps.hiddenTag = null;
            return true;
        }
        if (isClose || selfClosed || !IsHiddenCss(InlineAttr(tagStr, "style") ?? "")) return false;
        ps.hiddenTag = tag;
        ps.hiddenDepth = 1;
        return true;
    }

    /// <summary>Open a block: close the piece before it (a paragraph inside a paragraph
    /// closes the outer one), push the block's style and set its box rules from its tag.</summary>
    private static void OpenFlowBlock(HtmlFlowParseState ps, string tag, string tagStr)
    {
        if (tag == "p" && ps.cur.Kind == HtmlFlowBlockKind.Paragraph) CloseFlowBlock(ps);
        else FlushFlowBlock(ps, trailing: false);
        var style = InlineElementStyle(ps.stack[^1], tagStr, tag);
        var b = ps.cur;
        b.StyleDepth = ps.stack.Count;
        var attrStyle = InlineAttr(tagStr, "style") ?? "";
        var am = Regex.Match(attrStyle, @"text-align\s*:\s*(left|right|center)", RegexOptions.IgnoreCase);
        if (am.Success)
            b.Align = am.Groups[1].Value.ToLowerInvariant() switch
            { "right" => HorizontalAlignment.Right, "center" => HorizontalAlignment.Center, _ => HorizontalAlignment.Left };
        b.HeightPt = InlineCssLength(attrStyle, "height") ?? 0;
        var basePt = ps.stack[^1].SizePt > 0 ? ps.stack[^1].SizePt : ps.basePt;
        b.FontPt = style.SizePt > 0 ? style.SizePt : basePt;
        switch (tag)
        {
            case "p":
                b.Kind = HtmlFlowBlockKind.Paragraph;
                if (ps.paragraphMargins) b.MarginTop = b.MarginBottom = UaParagraphMarginEm * b.FontPt;
                break;
            case "div":
                b.Kind = HtmlFlowBlockKind.Div;
                break;
            case "li":
                b.Kind = HtmlFlowBlockKind.ListItem;
                OpenFlowListItem(ps, b);
                break;
            default:
                b.Kind = HtmlFlowBlockKind.Heading;
                var level = tag[1] - '0';
                if (style.SizePt <= 0) style.SizePt = basePt * UaHeadingSizeEm[level];
                style.Bold = true;
                b.FontPt = style.SizePt;
                b.MarginTop = b.MarginBottom = UaHeadingMarginEm[level] * b.FontPt;
                break;
        }
        // The block's own CSS margins replace its UA ones (a section report's
        // paragraphs declare margin: 0 and stack on the bare 13.5 line boxes).
        var (cssTop, cssBottom, _, _) = BlockCssMargins(attrStyle, b.FontPt);
        if (!double.IsNaN(cssTop)) b.MarginTop = cssTop;
        if (!double.IsNaN(cssBottom)) b.MarginBottom = cssBottom;
        ps.stack.Add(style);
    }

    private static void OpenFlowListItem(HtmlFlowParseState ps, HtmlFlowBlock b)
    {
        if (ps.lists.Count == 0) ps.lists.Add((false, 0, UaListIndentPt));
        var (ordered, counter, indent) = ps.lists[^1];
        counter++;
        ps.lists[^1] = (ordered, counter, indent);
        b.IndentPt = indent;
        b.Marker = ordered ? counter + "." : UaBulletMarker;
        if (counter == 1 && ps.lists.Count == 1) b.MarginTop = UaListMarginEm * ps.basePt;
    }

    private static void OpenFlowList(HtmlFlowParseState ps, bool ordered)
    {
        FlushFlowBlock(ps, trailing: false);
        var indent = (ps.lists.Count > 0 ? ps.lists[^1].Indent : 0) + UaListIndentPt;
        ps.lists.Add((ordered, 0, indent));
    }

    /// <summary>Closing the outermost list spends the list box's margin under its last item.</summary>
    private static void CloseFlowList(HtmlFlowParseState ps)
    {
        FlushFlowBlock(ps, trailing: false);
        if (ps.lists.Count > 0) ps.lists.RemoveAt(ps.lists.Count - 1);
        if (ps.lists.Count == 0 && ps.blocks.Count > 0 && ps.blocks[^1].Kind == HtmlFlowBlockKind.ListItem)
            ps.blocks[^1].MarginBottom = System.Math.Max(ps.blocks[^1].MarginBottom, UaListMarginEm * ps.basePt);
    }

    private static void CloseFlowBlock(HtmlFlowParseState ps)
    {
        if (ps.cur.Kind == HtmlFlowBlockKind.Anonymous) return;
        var depth = ps.cur.StyleDepth;
        FlushFlowBlock(ps, trailing: false);
        while (ps.stack.Count > depth && ps.stack.Count > 1) ps.stack.RemoveAt(ps.stack.Count - 1);
    }

    /// <summary>Seal the current block. An anonymous piece with no content (whitespace only) is kept only as
    /// the fragment's trailing piece when it holds an element (it then opens one empty
    /// line box); an element block is kept even when empty.</summary>
    private static void FlushFlowBlock(HtmlFlowParseState ps, bool trailing)
    {
        var b = ps.cur;
        var content = b.Items.Exists(it => it.Kind != HtmlInlineKind.Text || it.Text.Trim().Length > 0);
        var keep = b.Kind != HtmlFlowBlockKind.Anonymous || content || (trailing && b.HasElement && ps.blocks.Count > 0);
        if (keep)
        {
            // An anonymous piece of bare inter-block whitespace opens no line.
            if (b.Kind == HtmlFlowBlockKind.Anonymous && !content) b.Items.Clear();
            ps.blocks.Add(b);
        }
        ps.cur = new HtmlFlowBlock();
    }

    private static void AddInlineText(List<HtmlInlineItem> items, HtmlInlineStyle st, string raw)
    {
        var text = HtmlInlineWhitespace.Replace(System.Net.WebUtility.HtmlDecode(raw), " ");
        if (text.Length == 0) return;
        items.Add(new HtmlInlineItem
        {
            Kind = HtmlInlineKind.Text, Text = text, Family = st.Family, Bold = st.Bold, Italic = st.Italic,
            Underline = st.Underline, SizePt = st.SizePt, Fore = st.Fore, Back = st.Back, Href = st.Href,
        });
    }

    private static void AddInlineControl(List<HtmlInlineItem> items, string tagStr)
    {
        var type = InlineAttr(tagStr, "type")?.ToLowerInvariant() ?? "text";
        if (type is not ("radio" or "checkbox")) return;
        items.Add(new HtmlInlineItem
        {
            Kind = type == "radio" ? HtmlInlineKind.Radio : HtmlInlineKind.Checkbox,
            ControlName = InlineAttr(tagStr, "name"),
            Checked = Regex.IsMatch(tagStr, @"\bchecked\b", RegexOptions.IgnoreCase),
        });
    }

    /// <summary>A picture: its bytes from a data URI or a file under the fragment's base
    /// path, its box from the tag's width/height (CSS px) else its natural pixel size.</summary>
    private static void AddInlineImage(List<HtmlInlineItem> items, HtmlInlineStyle st, string tagStr, HtmlFragmentLayoutState hl)
    {
        var src = InlineAttr(tagStr, "src");
        if (string.IsNullOrEmpty(src)) return;
        var bytes = LoadInlineImageBytes(src, hl.html.HtmlLoadOptions?.BasePath);
        if (bytes is null) return;
        double natW = 0, natH = 0;
        if (TryGetImageNaturalSizePt(bytes, applyResolution: false) is (var pw, var ph)) { natW = pw * CssPxToPt; natH = ph * CssPxToPt; }
        var style = InlineAttr(tagStr, "style") ?? "";
        var w = InlineCssLength(style, "width") ?? ParseHtmlImgDimension(tagStr, "width") * CssPxToPt;
        var h = InlineCssLength(style, "height") ?? ParseHtmlImgDimension(tagStr, "height") * CssPxToPt;
        if (w <= 0 && h <= 0) { w = natW; h = natH; }
        else if (h <= 0) h = natW > 0 ? w * natH / natW : w;
        else if (w <= 0) w = natH > 0 ? h * natW / natH : h;
        if (w <= 0 || h <= 0) return;
        items.Add(new HtmlInlineItem
        {
            Kind = HtmlInlineKind.Image, ImageData = bytes, ImageW = w, ImageH = h, Href = st.Href,
            ImageMiddle = Regex.IsMatch(style, @"vertical-align\s*:\s*middle", RegexOptions.IgnoreCase),
        });
    }

    private static byte[]? LoadInlineImageBytes(string src, string? basePath)
    {
        try
        {
            var dm = Regex.Match(src, @"^data:[^;,]*;base64,(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (dm.Success) return System.Convert.FromBase64String(Regex.Replace(dm.Groups[1].Value, @"\s+", ""));
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            var path = IO.CallerPaths.FileUriToPath(src);
            if (!System.IO.Path.IsPathRooted(path) && !string.IsNullOrEmpty(basePath)) path = System.IO.Path.Combine(basePath, path);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        }
        catch { return null; }
    }

    /// <summary>A CSS length in <paramref name="style"/> for <paramref name="prop"/>, in points; null when absent.</summary>
    private static double? InlineCssLength(string style, string prop)
    {
        var m = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*([\d.]+)\s*(px|pt)?", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return null;
        return m.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? v : v * CssPxToPt;
    }

    private static string? InlineAttr(string tagStr, string name)
    {
        var m = Regex.Match(tagStr, @"\b" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }

    /// <summary>The style an element resolves for its content: its parent's, then its tag
    /// (b/strong, i/em, u, a) and its attributes (face, size, color, href), then its style
    /// attribute; a style attribute whose quoting is unbalanced applies nothing.</summary>
    private static HtmlInlineStyle InlineElementStyle(HtmlInlineStyle parent, string tagStr, string tag)
    {
        var st = parent.Clone();
        switch (tag)
        {
            case "b" or "strong": st.Bold = true; break;
            case "i" or "em": st.Italic = true; break;
            case "u": st.Underline = true; break;
            case "a":
                if (InlineAttr(tagStr, "href") is { Length: > 0 } href)
                { st.Href = href; st.Fore = HtmlLinkColor; st.Underline = true; }
                break;
            case "font":
                if (InlineAttr(tagStr, "face") is { Length: > 0 } face) st.Family = face.Split(',')[0].Trim().Trim('\'', '"');
                if (InlineAttr(tagStr, "size") is { Length: > 0 } size) st.SizePt = HtmlFontTagSizePt(size);
                if (InlineAttr(tagStr, "color") is { Length: > 0 } fc && Converters.HtmlToPdfConverter.ParseCssColor(fc) is { } fcol) st.Fore = fcol;
                break;
        }
        var sm = Regex.Match(tagStr, @"\bstyle\s*=\s*(['""])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (sm.Success) ApplyInlineCssStyle(st, sm.Groups[2].Value, sm.Groups[1].Value[0]);
        return st;
    }

    private static void ApplyInlineCssStyle(HtmlInlineStyle st, string style, char quote)
    {
        var other = quote == '"' ? '\'' : '"';
        var otherCount = 0;
        foreach (var c in style) if (c == other) otherCount++;
        if (otherCount % 2 == 1 || (quote != '\0' && style.Contains(quote))) return;
        var fm = Regex.Match(style, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            var first = fm.Groups[1].Value.Split(',')[0].Trim().Trim('\'', '"');
            if (first.Length > 0) st.Family = first;
        }
        var zm = Regex.Match(style, @"(?<![-\w])font-size\s*:\s*([\d.]+)\s*(px|pt|em|%)?", RegexOptions.IgnoreCase);
        if (zm.Success && double.TryParse(zm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fz) && fz > 0)
        {
            var parentPt = st.SizePt > 0 ? st.SizePt : UaInlineBasePt;
            st.SizePt = zm.Groups[2].Value.ToLowerInvariant() switch
            {
                "px" => fz * CssPxToPt,
                "em" => fz * parentPt,
                "%" => fz * parentPt / 100.0,
                _ => fz,
            };
        }
        if (Regex.IsMatch(style, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)) st.Bold = true;
        else if (Regex.IsMatch(style, @"font-weight\s*:\s*(normal|[1-5]00)", RegexOptions.IgnoreCase)) st.Bold = false;
        if (Regex.IsMatch(style, @"font-style\s*:\s*(italic|oblique)", RegexOptions.IgnoreCase)) st.Italic = true;
        else if (Regex.IsMatch(style, @"font-style\s*:\s*normal", RegexOptions.IgnoreCase)) st.Italic = false;
        if (Regex.IsMatch(style, @"text-decoration\s*:\s*[^;]*underline", RegexOptions.IgnoreCase)) st.Underline = true;
        else if (Regex.IsMatch(style, @"text-decoration\s*:\s*none", RegexOptions.IgnoreCase)) st.Underline = false;
        var cm = Regex.Match(style, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (cm.Success && Converters.HtmlToPdfConverter.ParseCssColor(cm.Groups[1].Value.Trim()) is { } fc) st.Fore = fc;
        var bm = Regex.Match(style, @"background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (bm.Success && Converters.HtmlToPdfConverter.ParseCssColor(bm.Groups[1].Value.Trim()) is { } bc) st.Back = bc;
    }

    /// <summary>The points a <c>&lt;font size&gt;</c> value resolves to: its leading digits
    /// (a unit suffix is ignored, "8px" reads as 8), +N/-N relative to 3, clamped to 1..7.</summary>
    private static double HtmlFontTagSizePt(string value)
    {
        var v = value.Trim();
        var m = Regex.Match(v, @"^([+-]?)(\d+)");
        if (!m.Success) return 0;
        var scale = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (m.Groups[1].Value == "+") scale = 3 + scale;
        else if (m.Groups[1].Value == "-") scale = 3 - scale;
        return HtmlFontSizeStepsPt[Compat.Clamp(scale, 1, 7)];
    }
}
